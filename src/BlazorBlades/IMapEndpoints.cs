using Microsoft.AspNetCore.Routing;

namespace BlazorBlades
{
    public interface IMapEndpoints
    {
        static abstract void MapEndpoints(IEndpointRouteBuilder app);
    }
}
