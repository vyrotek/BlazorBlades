using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorBlades
{
    internal class FragmentComponent : ComponentBase
    {
        [Parameter]
        public required RenderFragment RenderFragment { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.AddContent(0, RenderFragment);
        }
    }
}
