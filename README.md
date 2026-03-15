# Blazor Blades

[![Nuget](https://img.shields.io/nuget/v/BlazorBlades)](https://www.nuget.org/packages/BlazorBlades/)

## TL;DR

Conveniently render strongly-typed Razor Components and Fragments via Minimal APIs

## Try It

Add NuGet

``` shell
> dotnet add package BlazorBlades
```

Add these to your `Component.razor`

- Add `@inherits BlazorBlade<Model>` 
- Add `@implements IMapEndpoints`
- Implement `MapEndpoints()` in `@code{ }` or `Component.razor.cs`

``` csharp
// MyComponent.razor
@inherits BlazorBlade<Model> // Generates @Model, Blade(), RenderAsync()
@implements IMapEndpoints // Provides MapEndpoints()

// Generated @Model Property 
<div>Hello @Model.Name !</div>

@code
{
  public static void MapEndpoints(IEndpointRouteBuilder app)
  {
    app.MapGet("/component", () => 
    {
      var model = new Model("Jason");

      // Strongly-Typed RazorComponentResult
      return MyComponent.Blade(model); 
    });

    app.MapGet("/fragment", async () =>
    {
        var message = "Developers, Developers, Developers!"

        // Inline Fragment Result
        return Results.Razor
        (
            @<div>
                Quote: @message
            </div>
        );
    });

    app.MapGet("/raw", (IServiceProvider services) => 
    {
      // Render Component      
      var html = await MyComponent.RenderAsync(services, new Model("Jason"));

      // Render Fragment
      var html = await HelperFragments.SayHello("Julia").RenderAsync()
      ...
    });
  }
}
```

Add `app.MapEndpoints()` to `program.cs` to map all `IMapEndpoints` components

``` csharp
var app = builder.Build();
...
app.MapEndpoints();
...
```

## Demo

The sample web app in [Components/Page.razor](example/Components/Page.razor) has several examples.

## What

Blazor Blades is an experimental project inspired by [RazorSlices](https://github.com/DamianEdwards/RazorSlices), but built around Blazor `.razor` components instead of Razor `.cshtml` templates. It's aimed at devs looking for a .NET hypermedia workflow that is strongly typed end-to-end while keeping the benefits of Blazor components.

## Why

I wanted a way to build HTML-first applications using Minimal APIs with:

- Compile-time type safety for the data each rendered template requires
- Template composition and reuse through components and functions
- Locality of template, model, and endpoint code
- Any Javascript library for interactivity (Datastar, Htmx, jQuery, etc.)

## How

BlazorBlades lets a `.razor` component opt into generated capabilities:

- `@inherits BlazorBlade<TModel>` provides a Static `Component.Blade(model)`
- `@implements IMapEndpoints` provides a Static `Component.RenderAsync(services, model)`
- `app.MapEndpoints()` automatically calls `Component.MapEndpoints(app)`

Two component marker interfaces and source generators are used to accomplish this:

BlazorBlades also makes working with `RenderFragement` easier with:

- `Results.Razor(fragment)` 
- `fragment.RenderAsync(services)`
- `fragment.Blade()`

## Who

I'm [Jason Barnes](https://vyrotek.com) - Follow me: [@vyrotek](https://x.com/vyrotek)
