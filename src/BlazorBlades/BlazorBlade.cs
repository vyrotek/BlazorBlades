using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http.HttpResults;

namespace BlazorBlades
{
    public abstract partial class BlazorBlade<TModel> : ComponentBase
    {
        [Parameter]
        public virtual required TModel Model { get; set; }

        public static RazorComponentResult<TBlade> Blade<TBlade>(TModel model, int statusCode = 200)
            where TBlade : BlazorBlade<TModel>
            => BladeRendering.CreateResult<TBlade>(
                new Dictionary<string, object?> { [nameof(Model)] = model },
                statusCode: statusCode
            );

        public static Task<string> RenderAsync<TBlade>(IServiceProvider services, TModel model)
            where TBlade : BlazorBlade<TModel>
            => BladeRendering.RenderAsync<TBlade>(
                services,
                new Dictionary<string, object?> { [nameof(Model)] = model }
            );
    }

    public abstract partial class BlazorBlade : ComponentBase
    {
        public static RazorComponentResult<TBlade> Blade<TBlade>(int statusCode = 200)
            where TBlade : BlazorBlade
            => BladeRendering.CreateResult<TBlade>(statusCode: statusCode);

        public static Task<string> RenderAsync<TBlade>(IServiceProvider services)
            where TBlade : BlazorBlade
            => BladeRendering.RenderAsync<TBlade>(services);
    }
}
