
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http.HttpResults;

namespace BlazorBlades
{
    public interface IBlazorBlade<TModel> : IComponent
    {
        public TModel Model { get; set; }

        static abstract RazorComponentResult<TBlade> Blade<TBlade>(TModel model, int statusCode = 200) 
            where TBlade : BlazorBlade<TModel>;
    }

    public interface IBlazorBlade : IComponent
    {
        static abstract RazorComponentResult<TBlade> Blade<TBlade>(int statusCode = 200) 
            where TBlade : BlazorBlade;
    }
}