using BlazorBlades;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Microsoft.AspNetCore.Components
{
    public static class RenderFragmentExtensions
    {
        extension(RenderFragment fragment)
        {
            public RazorComponentResult<FragmentComponent> Blade(int statusCode = 200)
                => BladeRendering.CreateResult<FragmentComponent>(
                    new Dictionary<string, object?>
                    {
                        [nameof(FragmentComponent.RenderFragment)] = fragment
                    },
                    statusCode: statusCode
                );

            public Task<string> RenderAsync(IServiceProvider services)
                => BladeRendering.RenderAsync<FragmentComponent>(
                    services,
                    new Dictionary<string, object?>
                    {
                        [nameof(FragmentComponent.RenderFragment)] = fragment
                    }
                );
        }
    }
}
