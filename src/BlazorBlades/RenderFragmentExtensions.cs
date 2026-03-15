using BlazorBlades;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Components
{
    public static class RenderFragmentExtensions
    {
        extension(RenderFragment fragment)
        {
            public RazorComponentResult<FragmentComponent> Blade(int statusCode = 200)
            {
                return new RazorComponentResult<FragmentComponent>
                (
                    new Dictionary<string, object?>
                    {
                        [nameof(FragmentComponent.RenderFragment)] = fragment
                    }
                )
                {
                    PreventStreamingRendering = true,
                    StatusCode = statusCode
                };
            }

            public async Task<string> RenderAsync(IServiceProvider services)
            {
                var loggerFactory = services.GetRequiredService<ILoggerFactory>();
                await using var renderer = new HtmlRenderer(services, loggerFactory);

                return await renderer.Dispatcher.InvokeAsync(async () =>
                {
                    var root = await renderer.RenderComponentAsync<FragmentComponent>
                    (
                        ParameterView.FromDictionary(new Dictionary<string, object?>
                        {
                            [nameof(FragmentComponent.RenderFragment)] = fragment
                        })
                    );

                    await root.QuiescenceTask;
                    return root.ToHtmlString();
                });
            }
        }
    }
}