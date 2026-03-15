using BlazorBlades;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Microsoft.AspNetCore.Http
{
    public static class ResultsExtensions
    {
        extension(Results)
        {
            public static RazorComponentResult Fragment(RenderFragment fragment, string? contentType = null, int statusCode = 200)
                => BladeRendering.CreateResult<FragmentComponent>(
                    new Dictionary<string, object?>
                    {
                        [nameof(FragmentComponent.RenderFragment)] = fragment
                    },
                    contentType,
                    statusCode
                );
        }
    }
}
