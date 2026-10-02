using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace AxoClient.UI.Controls;

public sealed class Icon : FrameworkElement
{
    private sealed record Glyph(double View, double Stroke, bool Fill, Geometry Data);

    private static readonly Dictionary<string, Glyph> Glyphs = new(StringComparer.OrdinalIgnoreCase);

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(string),
        typeof(Icon), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(nameof(Size), typeof(double),
        typeof(Icon), new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure |
                                                          FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeWidthProperty = DependencyProperty.Register(nameof(StrokeWidth),
        typeof(double), typeof(Icon), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(Icon),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.Inherits |
                                                     FrameworkPropertyMetadataOptions.AffectsRender));

    static Icon()
    {
        Add("User", 20, 1.5, "M6.8 7 A3.2 3.2 0 1 1 13.2 7 A3.2 3.2 0 1 1 6.8 7 Z M4 17 c0 -3.3 2.7 -5.5 6 -5.5 s6 2.2 6 5.5");
        Add("Home", 20, 1.5, "M3 9.5 L10 3.5 l7 6 V16 a1 1 0 0 1 -1 1 h-3.5 v-5 h-5 v5 H4 a1 1 0 0 1 -1 -1 z");
        Add("Library", 20, 1.5, "M3 4.5 H5 A0.5 0.5 0 0 1 5.5 5 V16 A0.5 0.5 0 0 1 5 16.5 H3 A0.5 0.5 0 0 1 2.5 16 V5 A0.5 0.5 0 0 1 3 4.5 Z M7 3 H9 A0.5 0.5 0 0 1 9.5 3.5 V16 A0.5 0.5 0 0 1 9 16.5 H7 A0.5 0.5 0 0 1 6.5 16 V3.5 A0.5 0.5 0 0 1 7 3 Z M11 5.5 H13 A0.5 0.5 0 0 1 13.5 6 V16 A0.5 0.5 0 0 1 13 16.5 H11 A0.5 0.5 0 0 1 10.5 16 V6 A0.5 0.5 0 0 1 11 5.5 Z M14.3 6.6 l2.4 -.6 2.6 10 -2.4 .6 z M1.5 17.5 h17");
        Add("Server", 20, 1.5, "M4.5 3 H15.5 A1.5 1.5 0 0 1 17 4.5 V7.5 A1.5 1.5 0 0 1 15.5 9 H4.5 A1.5 1.5 0 0 1 3 7.5 V4.5 A1.5 1.5 0 0 1 4.5 3 Z M4.5 11 H15.5 A1.5 1.5 0 0 1 17 12.5 V15.5 A1.5 1.5 0 0 1 15.5 17 H4.5 A1.5 1.5 0 0 1 3 15.5 V12.5 A1.5 1.5 0 0 1 4.5 11 Z M6 6 h.01 M6 14 h.01 M10 6 h4 M10 14 h4");
        Add("Console", 20, 1.5, "M4.5 3.5 H15.5 A2 2 0 0 1 17.5 5.5 V14.5 A2 2 0 0 1 15.5 16.5 H4.5 A2 2 0 0 1 2.5 14.5 V5.5 A2 2 0 0 1 4.5 3.5 Z M6 8.5 l2.5 2 -2.5 2 M10.5 12.5 H14");
        Add("Gear", 24, 1.8, "M12.22 2 h-.44 a2 2 0 0 0 -2 2 v.18 a2 2 0 0 1 -1 1.73 l-.43 .25 a2 2 0 0 1 -2 0 l-.15 -.08 a2 2 0 0 0 -2.73 .73 l-.22 .38 a2 2 0 0 0 .73 2.73 l.15 .1 a2 2 0 0 1 1 1.72 v.51 a2 2 0 0 1 -1 1.74 l-.15 .09 a2 2 0 0 0 -.73 2.73 l.22 .38 a2 2 0 0 0 2.73 .73 l.15 -.08 a2 2 0 0 1 2 0 l.43 .25 a2 2 0 0 1 1 1.73 V20 a2 2 0 0 0 2 2 h.44 a2 2 0 0 0 2 -2 v-.18 a2 2 0 0 1 1 -1.73 l.43 -.25 a2 2 0 0 1 2 0 l.15 .08 a2 2 0 0 0 2.73 -.73 l.22 -.39 a2 2 0 0 0 -.73 -2.73 l-.15 -.08 a2 2 0 0 1 -1 -1.74 v-.5 a2 2 0 0 1 1 -1.74 l.15 -.09 a2 2 0 0 0 .73 -2.73 l-.22 -.38 a2 2 0 0 0 -2.73 -.73 l-.15 .08 a2 2 0 0 1 -2 0 l-.43 -.25 a2 2 0 0 1 -1 -1.73 V4 a2 2 0 0 0 -2 -2 z M9 12 A3 3 0 1 1 15 12 A3 3 0 1 1 9 12 Z");
        Add("WinMin", 26, 1.5, "M8 13 h10");
        Add("WinMax", 26, 1.5, "M8.5 8.5 h9 v9 h-9 z");
        Add("WinRestore", 26, 1.5, "M10.5 8.5 h7 v7 M8.5 10.5 h7 v7 h-7 z");
        Add("WinClose", 26, 1.5, "M8 8 l10 10 M18 8 L8 18");

        Add("Share", 16, 1.5, "M10.2 3.5 A1.8 1.8 0 1 1 13.8 3.5 A1.8 1.8 0 1 1 10.2 3.5 Z M2.2 8 A1.8 1.8 0 1 1 5.8 8 A1.8 1.8 0 1 1 2.2 8 Z M10.2 12.5 A1.8 1.8 0 1 1 13.8 12.5 A1.8 1.8 0 1 1 10.2 12.5 Z M5.6 7.1 l4.8 -2.7 M5.6 8.9 l4.8 2.7");
        Add("Users", 16, 1.6, "M3.5 5 A2.5 2.5 0 1 1 8.5 5 A2.5 2.5 0 1 1 3.5 5 Z M1.5 13.5 c0 -2.5 2 -4 4.5 -4 s4.5 1.5 4.5 4 M11 3.2 a2.4 2.4 0 0 1 0 4.4 M12.5 9.6 c1.3 .5 2 1.8 2 3.9");
        Add("UserAdd", 16, 1.5, "M3.5 5 A2.5 2.5 0 1 1 8.5 5 A2.5 2.5 0 1 1 3.5 5 Z M1.5 13.5 c0 -2.5 2 -4 4.5 -4 s4.5 1.5 4.5 4 M13 5 v5 M10.5 7.5 h5");
        Add("Person", 16, 1.5, "M5.2 5.5 A2.8 2.8 0 1 1 10.8 5.5 A2.8 2.8 0 1 1 5.2 5.5 Z M2.5 14 c0 -3 2.5 -4.8 5.5 -4.8 s5.5 1.8 5.5 4.8");
        Add("Search", 16, 1.5, "M2.5 7 A4.5 4.5 0 1 1 11.5 7 A4.5 4.5 0 1 1 2.5 7 Z M10.5 10.5 L14 14");
        Add("Ban", 16, 1.5, "M2.5 8 A5.5 5.5 0 1 1 13.5 8 A5.5 5.5 0 1 1 2.5 8 Z M4.2 11.8 l7.6 -7.6");
        Add("Clock", 16, 1.5, "M2 8 A6 6 0 1 1 14 8 A6 6 0 1 1 2 8 Z M8 4.8 V8 l2 1.3");
        Add("Globe", 16, 1.5, "M2 8 A6 6 0 1 1 14 8 A6 6 0 1 1 2 8 Z M2 8 h12 M8 2 c1.8 1.7 2.6 3.7 2.6 6 S9.8 12.3 8 14 M8 2 C6.2 3.7 5.4 5.7 5.4 8 s.8 4.3 2.6 6");
        Add("Info", 16, 1.5, "M2 8 A6 6 0 1 1 14 8 A6 6 0 1 1 2 8 Z M8 7.2 v4 M8 4.8 v.01");
        Add("Pipette", 16, 1.5, "M10 2.8 a1.9 1.9 0 0 1 2.7 2.7 l-1.3 1.3 .8 .8 -1.1 1.1 -3.8 -3.8 1.1 -1.1 .8 .8 z M7.9 6.4 L3 11.3 V13 h1.7 l4.9 -4.9");
        Add("ChevronLeft", 16, 1.6, "M10 3.5 L5.5 8 l4.5 4.5");
        Add("ChevronRight", 16, 1.8, "M6 3.5 L10.5 8 6 12.5");
        Add("ChevronUp", 16, 1.5, "M4 10 l4 -4 4 4");
        Add("ChevronDown", 16, 1.5, "M4 6 l4 4 4 -4");
        Add("Wrench", 16, 1.5, "M10.2 2.3 a3.2 3.2 0 0 0 -3.9 4.1 L2.6 10.1 a1.4 1.4 0 0 0 2 2 l3.7 -3.7 a3.2 3.2 0 0 0 4.1 -3.9 l-1.9 1.9 -1.6 -.4 -.4 -1.6 z");
        Add("Pencil", 16, 1.5, "M10.5 2.5 l3 3 L5.5 13.5 H2.5 v-3 z M9 4 l3 3");
        Add("Brush", 16, 1.5, "M10.5 2.5 l3 3 L5.5 13.5 H2.5 v-3 z");
        Add("Refresh", 16, 1.5, "M13.5 8 a5.5 5.5 0 0 1 -9.6 3.7 M2.5 8 a5.5 5.5 0 0 1 9.6 -3.7 M12.5 1.8 v2.7 H9.8 M3.5 14.2 v-2.7 h2.7");
        Add("Send", 16, 1.6, "M14 2 L7 9 M14 2 l-4.5 12 -2.5 -5 -5 -2.5 z");
        Add("Folder", 16, 1.5, "M2 4.5 A1.5 1.5 0 0 1 3.5 3 h2.6 l1.4 1.5 h5 A1.5 1.5 0 0 1 14 6 v5.5 a1.5 1.5 0 0 1 -1.5 1.5 h-9 A1.5 1.5 0 0 1 2 11.5 z");
        Add("Trash", 16, 1.5, "M2.5 4.5 h11 M6 4.5 V3 a1 1 0 0 1 1 -1 h2 a1 1 0 0 1 1 1 v1.5 M4 4.5 l.8 8.5 a1 1 0 0 0 1 1 h4.4 a1 1 0 0 0 1 -1 l.8 -8.5 M6.75 7 v4.5 M9.25 7 v4.5");
        Add("Reset", 16, 1.5, "M2.5 8 a5.5 5.5 0 1 0 1.6 -3.9 M2.5 2.5 v2.5 H5");
        Add("History", 16, 1.5, "M2.5 8 a5.5 5.5 0 1 0 1.6 -3.9 M2.5 2.5 v2.5 H5 M8 5.5 V8 l1.8 1.2");
        Add("Wand", 16, 1.5, "M3 13 l7.5 -7.5 M9 4 l3 3 M12 1.8 v2 M11 2.8 h2 M4.5 2 v2 M3.5 3 h2 M13.5 9 v2 M12.5 10 h2");
        Add("Shield", 16, 1.5, "M3 2.5 h10 v9.5 l-5 2 -5 -2 z");
        Add("ArrowRight", 16, 1.8, "M3 8 h10 M9.5 4.5 L13 8 l-3.5 3.5");
        Add("ArrowUp", 16, 1.8, "M8 13 V3 M4.5 6.5 L8 3 l3.5 3.5");
        Add("Check", 16, 2.6, "M3.5 8.5 l3 3 6 -7");
        Add("Bell", 16, 1.5, "M4 11 V7 a4 4 0 0 1 8 0 v4 l1 1 H3 z M6.5 14 c.3 .6 .9 1 1.5 1 s1.2 -.4 1.5 -1 M8 1.5 V3");
        Add("Close", 16, 1.7, "M4 4 l8 8 M12 4 l-8 8");
        Add("Play", 16, 1.5, "M5 3.5 v9 l7 -4.5 z", fill: true);
        Add("Stop", 16, 0, "M5 3.5 H11 A1.5 1.5 0 0 1 12.5 5 V11 A1.5 1.5 0 0 1 11 12.5 H5 A1.5 1.5 0 0 1 3.5 11 V5 A1.5 1.5 0 0 1 5 3.5 Z", fill: true);
        Add("List", 16, 1.5, "M5.5 4 h8.5 M5.5 8 h8.5 M5.5 12 h8.5 M2 4 h.5 M2 8 h.5 M2 12 h.5");
        Add("Grid", 16, 1.5, "M3 2 H6 A1 1 0 0 1 7 3 V6 A1 1 0 0 1 6 7 H3 A1 1 0 0 1 2 6 V3 A1 1 0 0 1 3 2 Z M10 2 H13 A1 1 0 0 1 14 3 V6 A1 1 0 0 1 13 7 H10 A1 1 0 0 1 9 6 V3 A1 1 0 0 1 10 2 Z M3 9 H6 A1 1 0 0 1 7 10 V13 A1 1 0 0 1 6 14 H3 A1 1 0 0 1 2 13 V10 A1 1 0 0 1 3 9 Z M10 9 H13 A1 1 0 0 1 14 10 V13 A1 1 0 0 1 13 14 H10 A1 1 0 0 1 9 13 V10 A1 1 0 0 1 10 9 Z");
        Add("Microsoft", 16, 1.6, "M3 2 H6.5 A1 1 0 0 1 7.5 3 V6.5 A1 1 0 0 1 6.5 7.5 H3 A1 1 0 0 1 2 6.5 V3 A1 1 0 0 1 3 2 Z M9.5 2 H13 A1 1 0 0 1 14 3 V6.5 A1 1 0 0 1 13 7.5 H9.5 A1 1 0 0 1 8.5 6.5 V3 A1 1 0 0 1 9.5 2 Z M3 8.5 H6.5 A1 1 0 0 1 7.5 9.5 V13 A1 1 0 0 1 6.5 14 H3 A1 1 0 0 1 2 13 V9.5 A1 1 0 0 1 3 8.5 Z M9.5 8.5 H13 A1 1 0 0 1 14 9.5 V13 A1 1 0 0 1 13 14 H9.5 A1 1 0 0 1 8.5 13 V9.5 A1 1 0 0 1 9.5 8.5 Z");
        Add("Logout", 16, 1.6, "M6 2.5 H3.5 a1 1 0 0 0 -1 1 v9 a1 1 0 0 0 1 1 H6 M10.5 11 L13.5 8 l-3 -3 M13.5 8 H6");
        Add("Join", 16, 1.6, "M6.5 2.5 h-3 a1 1 0 0 0 -1 1 v9 a1 1 0 0 0 1 1 h3 M14 8 H6 M9 5 l-3 3 3 3");
        Add("Import", 16, 1.5, "M9.5 2.5 h3 a1 1 0 0 1 1 1 v9 a1 1 0 0 1 -1 1 h-3 M2 8 h8 M7 5 l3 3 -3 3");
        Add("Link", 16, 1.8, "M6.5 9.5 l3 -3 M9 4 l1 -1 a2.8 2.8 0 0 1 4 4 l-1 1 M7 12 l-1 1 a2.8 2.8 0 0 1 -4 -4 l1 -1");
        Add("Bucket", 16, 1.5, "M7 2.5 l5.5 5.5 -4.5 4.5 a1.4 1.4 0 0 1 -2 0 L2.5 9 a1.4 1.4 0 0 1 0 -2 z M2.5 8 h9.8 M13.5 10.5 s1 1.4 1 2 a1 1 0 0 1 -2 0 c0 -.6 1 -2 1 -2 z");
        Add("Eraser", 16, 1.5, "M9 3 l4.5 4.5 -6 6 H4 l-2 -2 a1.4 1.4 0 0 1 0 -2 z M6.5 5.5 l4.5 4.5 M7 13.5 h7");
        Add("Warning", 16, 1.6, "M8 1.8 L14.6 13.5 H1.4 z M8 6.2 v3.3 M8 11.6 v.01");
        Add("Cube", 16, 1.5, "M8 1.8 l5.5 3.1 v6.2 L8 14.2 2.5 11.1 V4.9 z M2.5 4.9 L8 8 l5.5 -3.1 M8 8 v6.2");
        Add("Upload", 16, 1.6, "M8 11 V3 M4.5 6.5 L8 3 l3.5 3.5 M2.5 11 v1.5 a1 1 0 0 0 1 1 h9 a1 1 0 0 0 1 -1 V11");
        Add("Download", 16, 1.6, "M8 2.5 v7.5 M4.8 7 L8 10.2 11.2 7 M2.5 11.5 v1 a1 1 0 0 0 1 1 h9 a1 1 0 0 0 1 -1 v-1");
        Add("Layers", 16, 1.5, "M8 2 L2.5 5 8 8 l5.5 -3 z M2.5 8 L8 11 l5.5 -3 M2.5 11 L8 14 l5.5 -3");
        Add("Plus", 16, 1.6, "M8 3 v10 M3 8 h10");
        Add("Minus", 16, 1.6, "M3 8 h10");
        Add("Archive", 16, 1.5, "M3 2.5 H13 A1 1 0 0 1 14 3.5 V5 A1 1 0 0 1 13 6 H3 A1 1 0 0 1 2 5 V3.5 A1 1 0 0 1 3 2.5 Z M3 6 v6.5 a1 1 0 0 0 1 1 h8 a1 1 0 0 0 1 -1 V6 M6.5 9 h3");
        Add("Image", 16, 1.5, "M3.5 3 H12.5 A1.5 1.5 0 0 1 14 4.5 V11.5 A1.5 1.5 0 0 1 12.5 13 H3.5 A1.5 1.5 0 0 1 2 11.5 V4.5 A1.5 1.5 0 0 1 3.5 3 Z M4.8 6.5 A1.2 1.2 0 1 1 7.2 6.5 A1.2 1.2 0 1 1 4.8 6.5 Z M14 11 l-3.5 -3.5 L4 13");
        Add("Calendar", 16, 1.5, "M4 3.5 H12 A1.5 1.5 0 0 1 13.5 5 V12 A1.5 1.5 0 0 1 12 13.5 H4 A1.5 1.5 0 0 1 2.5 12 V5 A1.5 1.5 0 0 1 4 3.5 Z M2.5 6.5 h11 M5.5 2 v3 M10.5 2 v3");
        Add("Copy", 16, 1.5, "M7 5.5 H12 A1.5 1.5 0 0 1 13.5 7 V12 A1.5 1.5 0 0 1 12 13.5 H7 A1.5 1.5 0 0 1 5.5 12 V7 A1.5 1.5 0 0 1 7 5.5 Z M10.5 5.5 V4 a1.5 1.5 0 0 0 -1.5 -1.5 H4 A1.5 1.5 0 0 0 2.5 4 v5 A1.5 1.5 0 0 0 4 10.5 h1.5");
        Add("Star", 16, 1.5, "M8 1.8 l1.9 3.9 4.3 .6 -3.1 3 .7 4.3 L8 11.6 4.2 13.6 l.7 -4.3 -3.1 -3 4.3 -.6 z");
        Add("Cpu", 16, 1.5, "M5 4 H11 A1 1 0 0 1 12 5 V11 A1 1 0 0 1 11 12 H5 A1 1 0 0 1 4 11 V5 A1 1 0 0 1 5 4 Z M6.5 6.5 h3 v3 h-3 z M6 1.5 v2.5 M10 1.5 v2.5 M6 12 v2.5 M10 12 v2.5 M1.5 6 h2.5 M1.5 10 h2.5 M12 6 h2.5 M12 10 h2.5");
        Add("Map", 16, 1.5, "M2 4 l4 -1.5 4 1.5 4 -1.5 v9.5 l-4 1.5 -4 -1.5 -4 1.5 z M6 2.5 v9.5 M10 4 v9.5");
        Add("Key", 16, 1.5, "M2.5 10.5 A2.5 2.5 0 1 1 7.5 10.5 A2.5 2.5 0 1 1 2.5 10.5 Z M6.8 8.7 L13 2.5 M11 4.5 l1.5 1.5 M9.5 6 l1.5 1.5");
        Add("Pause", 16, 1.8, "M5.5 3.5 v9 M10.5 3.5 v9");
        Add("Eye", 16, 1.5, "M1.5 8 s2.5 -4.5 6.5 -4.5 6.5 4.5 6.5 4.5 -2.5 4.5 -6.5 4.5 S1.5 8 1.5 8 z M6 8 A2 2 0 1 1 10 8 A2 2 0 1 1 6 8 Z");
        Add("More", 16, 2.2, "M3.5 8 h.01 M8 8 h.01 M12.5 8 h.01");
        Add("External", 16, 1.5, "M9.5 2.5 h4 v4 M13.5 2.5 L7.5 8.5 M12 9.5 v3 a1 1 0 0 1 -1 1 H3.5 a1 1 0 0 1 -1 -1 V5 a1 1 0 0 1 1 -1 h3");
        Add("Filter", 16, 1.5, "M2 3 h12 l-4.5 5.5 v4.5 l-3 1.5 v-6 z");
        Add("Sort", 16, 1.5, "M4.5 2.5 v11 M2 11 l2.5 2.5 L7 11 M11.5 13.5 v-11 M9 5 l2.5 -2.5 L14 5");
        Add("Swap", 16, 1.5, "M2.5 5.5 h10 M10 3 l2.5 2.5 L10 8 M13.5 10.5 h-10 M6 8 l-2.5 2.5 L6 13");
        Add("Move", 16, 1.5, "M8 1.8 V14.2 M1.8 8 H14.2 M6 3.8 L8 1.8 10 3.8 M6 12.2 L8 14.2 10 12.2 M3.8 6 L1.8 8 3.8 10 M12.2 6 L14.2 8 12.2 10");
        Add("Terminal", 16, 1.5, "M3.5 3 H12.5 A1.5 1.5 0 0 1 14 4.5 V11.5 A1.5 1.5 0 0 1 12.5 13 H3.5 A1.5 1.5 0 0 1 2 11.5 V4.5 A1.5 1.5 0 0 1 3.5 3 Z M5 6.5 l2 1.5 -2 1.5 M8.5 10 H11");
        Add("Sparkle", 16, 1.5, "M8 1.5 c.6 3.3 1.7 4.6 5 5.5 -3.3 .9 -4.4 2.2 -5 5.5 -.6 -3.3 -1.7 -4.6 -5 -5.5 3.3 -.9 4.4 -2.2 5 -5.5 z");
    }

    public string Kind
    {
        get => (string)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public double StrokeWidth
    {
        get => (double)GetValue(StrokeWidthProperty);
        set => SetValue(StrokeWidthProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public static bool Exists(string kind) => Glyphs.ContainsKey(kind);

    private static void Add(string name, double view, double stroke, string data, bool fill = false)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        Glyphs[name] = new Glyph(view, stroke, fill, geometry);
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    protected override void OnRender(DrawingContext dc)
    {
        if (string.IsNullOrEmpty(Kind) || !Glyphs.TryGetValue(Kind, out var glyph))
            return;
        var scale = Size / glyph.View;
        dc.PushTransform(new ScaleTransform(scale, scale));
        var stroke = double.IsNaN(StrokeWidth) ? glyph.Stroke : StrokeWidth;
        Pen? pen = null;
        if (stroke > 0)
        {
            pen = new Pen(Foreground, stroke)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
        }
        dc.DrawGeometry(glyph.Fill ? Foreground : null, pen, glyph.Data);
        dc.Pop();
    }
}
