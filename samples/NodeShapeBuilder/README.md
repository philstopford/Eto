# NodeShapeBuilder

A **standalone** Eto demo application that demonstrates the **shape-generation and
boolean-operation** capabilities described in the DesignLibs project, wired together
using the `NodeGraphView` control from the main Eto repository.

## Purpose

The application proves that the `NodeGraphView` enhancements (socket pinning, control-flow
exec pins, live property panel) are capable of driving real geometry pipelines:

| Node type     | Description |
|---------------|-------------|
| Rectangle     | Width × Height rectangle, centred at the origin |
| Circle        | n-gon approximation of a circle (Radius, Segments) |
| L-Shape       | L-shaped polygon (outer rect minus notch) |
| T-Shape       | T-shaped polygon (bar + stem) |
| Union         | Clipper2 A ∪ B |
| Intersection  | Clipper2 A ∩ B |
| Difference    | Clipper2 A − B |
| Output        | Terminal node — its geometry is displayed in the preview panel |

## Usage

1. Click toolbar buttons to add shape or boolean nodes.
2. Drag from an output socket (circle on the right of a node) to an input socket
   (circle on the left) to connect them.
3. Select a node — the left **Properties** panel lists all its parameters with
   inline editable default values; the right **Preview** panel renders the
   evaluated polygon(s) using a dark-theme Eto `Drawable`.
4. Right-click a connection arrow to delete it.
5. Press **F** or click **Frame** to fit all nodes in view.

## Platform

The project is configured for **Gtk** (Linux/macOS).  To run on Windows, edit
`Program.cs` and change the platform initializer:

```csharp
// Windows Forms
new Application(new Eto.WinForms.Platform()).Run(new MainForm());

// WPF
new Application(new Eto.Wpf.Platform()).Run(new MainForm());
```

And update `NodeShapeBuilder.csproj` to reference `Eto.WinForms.csproj` or
`Eto.Wpf.csproj` instead of `Eto.Gtk.csproj`.

## Building

From the repository root:

```sh
dotnet build samples/NodeShapeBuilder/NodeShapeBuilder.csproj
dotnet run   --project samples/NodeShapeBuilder/NodeShapeBuilder.csproj
```

## Dependencies

| Package    | Version | Purpose |
|------------|---------|---------|
| Clipper2   | 1.4.0   | Polygon boolean operations (same library as DesignLibs) |
| Eto        | (source)| Cross-platform UI and NodeGraphView control |
| GtkSharp   | 3.24.x  | Platform handler for Linux/macOS |

## Relation to DesignLibs

DesignLibs (https://github.com/philstopford/DesignLibs_GPL) contains the full
`shapeEngine` and `geoWrangler` implementations used by Variance and Quilt.  This demo
re-implements a minimal subset of those shapes using the **same underlying
Clipper2 library**, verifying that the node graph infrastructure can produce and
display real geometry without requiring a full DesignLibs dependency.

When the full DesignLibs NuGet packages are published, the shape node factories in
`MainForm.cs` can be replaced with `ShapeLibrary.setShape(...)` calls with no changes
to the graph infrastructure.
