using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace AvalonMarkdown.Test.Android
{
    [Application]
    public class Application : AvaloniaAndroidApplication<AvalonMarkdown.Test.Shared.App>
    {
        protected Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
        }

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            return base.CustomizeAppBuilder(builder)
                .WithInterFont();
        }
    }
}
