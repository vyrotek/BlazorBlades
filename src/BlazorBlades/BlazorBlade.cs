using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BlazorBlades
{
    public abstract partial class BlazorBlade<TModel> : BlazorBladeBase, IBlazorBlade<TModel>
    {
        [Parameter]
        public virtual required TModel Model { get; set; }

        public static RazorComponentResult<TBlade> Blade<TBlade>(TModel model, int statusCode = 200)
            where TBlade : BlazorBlade<TModel>
        {
            return new RazorComponentResult<TBlade>
            (
                new Dictionary<string, object?>
                {
                    [nameof(Model)] = model
                }
            )
            {
                PreventStreamingRendering = true,
                StatusCode = statusCode
            };
        }

        public static async Task<string> RenderAsync<TBlade>(IServiceProvider services, TModel model)
            where TBlade : BlazorBlade<TModel>
        {
            var loggerFactory = services.GetRequiredService<ILoggerFactory>();
            await using var renderer = new HtmlRenderer(services, loggerFactory);

            return await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var root = await renderer.RenderComponentAsync<TBlade>
                (
                    ParameterView.FromDictionary(new Dictionary<string, object?>
                    {
                        [nameof(Model)] = model
                    })
                );

                await root.QuiescenceTask;
                return root.ToHtmlString();
            });
        }
    }

    public abstract partial class BlazorBlade : BlazorBladeBase, IBlazorBlade
    {
        public static RazorComponentResult<TBlade> Blade<TBlade>(int statusCode = 200)
            where TBlade : BlazorBlade
        {
            return new RazorComponentResult<TBlade>()
            {
                PreventStreamingRendering = true,
                StatusCode = statusCode
            };
        }

        public static async Task<string> RenderAsync<TBlade>(IServiceProvider services)
            where TBlade : BlazorBlade
        {
            var loggerFactory = services.GetRequiredService<ILoggerFactory>();
            await using var renderer = new HtmlRenderer(services, loggerFactory);

            return await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var root = await renderer.RenderComponentAsync<TBlade>();

                await root.QuiescenceTask;
                return root.ToHtmlString();
            });
        }
    }

    public abstract class BlazorBladeBase : ComponentBase { }
}