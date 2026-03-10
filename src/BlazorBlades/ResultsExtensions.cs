using BlazorBlades;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Microsoft.AspNetCore.Http
{
    public static class ResultsExtensions
    {
        extension(Results)
        {
            public static RazorComponentResult Razor(RenderFragment fragment, string? contentType = null, int? statusCode = null)
            {
                return new RazorComponentResult<FragmentComponent>(new Dictionary<string, object?>
                {
                    [nameof(FragmentComponent.RenderFragment)] = fragment
                })
                {
                    PreventStreamingRendering = true,
                    ContentType = contentType,
                    StatusCode = statusCode
                };
            }
        }
    }
}
