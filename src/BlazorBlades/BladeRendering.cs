using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BlazorBlades
{
    internal static class BladeRendering
    {
        public static RazorComponentResult<TComponent> CreateResult<TComponent>(
            IDictionary<string, object?>? parameters = null,
            string? contentType = null,
            int statusCode = 200
        )
            where TComponent : IComponent
        {
            var result = parameters is null
                ? new RazorComponentResult<TComponent>()
                : new RazorComponentResult<TComponent>(parameters);

            result.PreventStreamingRendering = true;
            result.ContentType = contentType;
            result.StatusCode = statusCode;

            return result;
        }

        public static async Task<string> RenderAsync<TComponent>(
            IServiceProvider services,
            IDictionary<string, object?>? parameters = null
        )
            where TComponent : IComponent
        {
            var loggerFactory = services.GetRequiredService<ILoggerFactory>();
            await using var renderer = new HtmlRenderer(services, loggerFactory);

            return await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var root = parameters is null
                    ? await renderer.RenderComponentAsync<TComponent>()
                    : await renderer.RenderComponentAsync<TComponent>(
                        ParameterView.FromDictionary(parameters)
                    );

                await root.QuiescenceTask;
                return root.ToHtmlString();
            });
        }
    }
}
