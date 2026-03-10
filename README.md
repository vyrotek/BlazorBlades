# Blazor Blades

![Icon](icon.png)

## TL;DR

Render strongly-typed Blazor components and fragments via Minimal APIs organized within .razor files

## Try It

Add NuGet

``` shell
> dotnet add package BlazorBlades
```

Add these interfaces to .razor components and map endpoints

``` csharp
@implements IRenderProps
@implements IMapEndpoints

@code
{
  public static void MapEndpoints(IEndpointRouteBuilder app)
  {
    app.MapGet("/component", () => 
    {
      // Generated Props
      var props = new ComponentNameProps();
      
      // Call Render with Props
      return ComponentName.Render(props); 
    });

    app.MapGet("/fragment", async () =>
    {
        // Inline Fragment
        return Results.Razor
        (
            @<div>
                Hello!
            </div>
        );
    });

  }
}
```

Add this to program.cs
``` csharp
var app = builder.Build();
...
app.MapEndpoints();
...
```

## What

Blazor Blades is an experimental project inspired by [RazorSlices](https://github.com/DamianEdwards/RazorSlices), but built around Blazor `.razor` components instead of Razor `.cshtml` templates.

It's aimed at devs looking for a .NET hypermedia workflow that is strongly typed end-to-end while keeping the benefits of Blazor components.

## Why

I wanted a way to build HTML-first applications using Minimal APIs with:

- Compile-time type safety for the data each rendered template requires
- Template composition and reuse through components and functions
- Locality of template, model, and endpoint code
- Any Javascript library for interactivity (Datastar, Htmx, jQuery, etc.)

## How

BlazorBlades lets a `.razor` component opt into generated capabilities:

- A strongly typed `ComponentProps` record plus a static `Component.Render(...)` method
- Automatic endpoint mapping via a single `app.MapEndpoints()` call

Two marker interfaces and source generators are used to accomplish this:

- [IRenderProps.cs](src/BlazorBlades/IRenderProps.cs): 
  - Marks a component that should get generated `Component.Render(props)` helper and `ComponentProps` record
- [IMapEndpoints.cs](src/BlazorBlades/IMapEndpoints.cs): 
  - Marks a component that exposes a static `MapEndpoints(app)` method to be called

BlazorBlades also provides a `Results.Razor(fragment)` helper to render template fragments 

## Demo

The sample web app in [Components/Page.razor](example/Components/Page.razor) shows exampes of pages, partials, and fragments.

## Who

I'm [Jason Barnes](https://vyrotek.com) - Follow me: [@vyrotek](https://x.com/vyrotek)