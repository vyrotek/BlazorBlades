using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorBlades
{
    public class FragmentComponent : BlazorBlade
    {
        [Parameter]
        public required RenderFragment RenderFragment { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.AddContent(0, RenderFragment);
        }
    }
}
