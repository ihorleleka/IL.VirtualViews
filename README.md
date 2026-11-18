[![NuGet version (IL.VirtualViews)](https://img.shields.io/nuget/v/IL.VirtualViews.svg?style=flat-square)](https://www.nuget.org/packages/IL.VirtualViews/)
# IL.VirtualViews

Define Razor templates inside feature slices and register them at virtual MVC view paths. Targets .NET 10 and .NET 11. **Compilation is build-time only**: no runtime compiler, fallback mode, temporary debug files or first-request compilation.

## Usage

1. Reference the `IL.VirtualViews` NuGet package in the project declaring the views.
2. Add a template, such as `Features/Reports/Report.virtual.cshtml`, with build action **AdditionalFiles**:

   ```xml
   <AdditionalFiles Include="Features/Reports/Report.virtual.cshtml" />
   ```

   ```razor
   @model string
   <p>@Model</p>
   ```

3. Define its matching partial class and registration path:

   ```csharp
   using IL.VirtualViews.Attributes;

   namespace MyApp.Features.Reports;

   [VirtualViewPath("/Views/Reports/Report.cshtml")]
   public partial class Report;
   ```

4. Register the library with the existing service or builder extension:

   ```csharp
   services.AddVirtualViewsCapabilities("MyApp*");
   // Alternatively: builder.AddVirtualViewsCapabilities("MyApp*");
   ```

The package supplies the compiler tool, generated `IVirtualView` companion classes, Razor build integration and MVC registration. **No consumer SDK change, explicit target import or separate tool installation is required.** Ordinary .NET SDK projects and existing Web/Razor SDK projects are supported. The build tool runs on .NET 10 or later.

The generated namespace follows the existing folder convention: starting at the last directory whose name prefixes the assembly name, join the remaining directories with dots; otherwise use the assembly name. The partial class is matched to that namespace and the template filename case-insensitively (including the `.virtual.cshtml` suffix). Generated code retains the class's declared C# casing. Templates with ambiguous case-insensitive identities are rejected. Implicit accessibility is supported for template-backed partials: the generated part is public. Classes must be top-level and non-generic.

## Linked templates

Templates outside the project remain supported:

```xml
<AdditionalFiles Include="../SharedTemplates/Report.virtual.cshtml" />
```

Paths containing `..` are normalized before deriving the companion namespace. Edits and removals update intermediate outputs on the next build without requiring a clean.

## Hand-written constant templates

For existing class-based templates, public `IVirtualView` classes can still declare constant content:

```csharp
[VirtualViewPath("/Views/Sample.cshtml")]
public class Sample : IVirtualView
{
    public static string ViewContent => "@model string\n<p>@Model</p>";
}
```

Expression-bodied properties, constant initializers and getters containing one constant return are supported. Arbitrary method calls and dynamic getters fail the build; move dynamic values into the Razor model. Consumer code is never executed to extract templates.

## Compilation and path rules

The build tool passes original `.virtual.cshtml` sources directly to the installed Razor SDK, which compiles them into the declaring assembly in both Debug and Release. Only hand-written constant templates require intermediate Razor sources. Invalid Razor and unresolved model types fail the build. MVC uses its normal compiled-view engine and cache; deployed applications need neither template sources nor a runtime C# compiler.

Registration maps compiled items to `VirtualViewPath`, including derived attributes. Repeated registration is supported; duplicate virtual paths and conflicts with ordinary compiled views are rejected. Paths must begin with `/`, end with `.cshtml`, and contain no traversal segments.

Templates compile beneath synthetic `/_VirtualViews/<fully-qualified-class-name>.cshtml` identifiers. Put shared `@using`, `@inject` and tag-helper directives in the project's root `_ViewImports.cshtml` or in each template. Folder-specific imports beneath the runtime path do not apply. Use absolute registered paths for layouts and partials, such as `/Views/Shared/Layout.cshtml`.

Rebuild after template changes. Build-time Razor source generation must stay enabled. Libraries declaring views must be rebuilt with these package assets; a host cannot retroactively compile templates inside a legacy binary.

## Debugging

Original `.virtual.cshtml` files (including linked ones) are fed to the Razor compiler by their real physical path, so the PDB records them as documents with their SHA-256 checksums. Set breakpoints directly in the template in your IDE; no temporary files are generated. Hand-written constant templates have no physical file and are materialized under `obj/<config>/<tfm>/VirtualViews/` for debugging. Rebuild after editing a template so the PDB and checksum match the source.

## Migration

The template + attribute + registration interface remains unchanged. The former runtime-only `VirtualViewsProvider`, `VirtualViewSourcePathAttribute`, debug physical-file option and configuration overloads have been removed. `VirtualViews:Debug` no longer applies. Use normal generated Razor source/PDB debugging instead.

The old analyzer has been replaced by a single build tool which owns both companion generation and Razor inputs. There is no runtime-compilation package dependency or compatibility mode.

## Tests

Run the normal repository commands:

```sh
dotnet build Tests/IL.VirtualViews.Tests.csproj --configuration Release
dotnet test Tests/IL.VirtualViews.Tests.csproj --configuration Release --no-build
```

Tests cover compiled rendering, typed models, derived attributes, linked templates, layouts/partials, registration, caching and concurrency. The build stages a package in the test output, and the .NET integration suite tests an isolated plain SDK package consumer, incremental changes/removals and build failures. It uses temporary directories and retained diagnostic logs; package restore requires access to the configured NuGet dependencies. No dedicated build-compilation script is required.

## Release versioning

The tag pipeline validates `[v]major.minor.patch[.revision][-prerelease][+metadata]` through the [shared-contract parser](.github/scripts/Set-ReleaseVersion.ps1) before restore/build/pack. It injects version properties consistently. NuGet omits build metadata from package identity, so changing only `+metadata` does not create a distinct release.

[Conditional defaults](Directory.Build.props) give local builds `0.0.0-dev`; explicit overrides remain supported. Existing tag-only publishing behavior is retained. Run the [parser regression tests](.github/scripts/Test-ReleaseVersion.ps1) when changing release versioning.
