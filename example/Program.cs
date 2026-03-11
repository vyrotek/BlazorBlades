using StarFederation.Datastar.DependencyInjection;

namespace BlazorBlades.Web
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddRazorComponents();

            builder.Services.AddDatastar();

            var app = builder.Build();

            if (!app.Environment.IsDevelopment())
            {
                app.UseHsts();
            }

            app.UseHttpsRedirection();

            app.UseAntiforgery();

            app.MapEndpoints();

            app.MapStaticAssets();

            app.Run();
        }
    }
}