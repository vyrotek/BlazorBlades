using BlazorBlades;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Components
{
    public static class RenderFragmentExtensions
    {
        extension(RenderFragment fragment)
        {
            public async Task<string> RenderAsync(IServiceProvider services)
            {
                var loggerFactory = services.GetRequiredService<ILoggerFactory>();
                using var renderer = new HtmlRenderer(services, loggerFactory);
                var root = await renderer.Dispatcher.InvokeAsync(() =>
                    renderer.RenderComponentAsync<FragmentComponent>(ParameterView.FromDictionary(
                        new Dictionary<string, object?> { [nameof(FragmentComponent.RenderFragment)] = fragment })));
                await root.QuiescenceTask;
                return await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
            }
        }
    }
}