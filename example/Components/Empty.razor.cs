namespace BlazorBlades.Web.Components
{
    public partial class Empty : IMapEndpoints
    {
        public static void MapEndpoints(IEndpointRouteBuilder app)
        {
            app.MapGet("/empty", async (IServiceProvider services) =>
            {
                return Empty.Blade();
            });
        }
    }
}
