using UIKit;

namespace Immons.Tools.Maui.Inspector.Features.Memory.Images;

internal static partial class ImageCensus
{
    static partial void CollectPlatform(List<(View Element, bool Attached)> elements, ref List<ImageInfo>? images)
    {
        images = [];
        foreach (var (element, attached) in elements)
        {
            var image = element.Handler?.PlatformView switch
            {
                UIImageView view => view.Image,
                UIButton button => button.CurrentImage,
                _ => null,
            };
            if (image == null)
                continue;
            var scale = image.CurrentScale <= 0 ? 1 : image.CurrentScale;
            var width = (int)(image.Size.Width * scale);
            var height = (int)(image.Size.Height * scale);
            images.Add(new ImageInfo(Owner(element), Source(element), width, height, (long)width * height * 4, attached));
        }
    }
}
