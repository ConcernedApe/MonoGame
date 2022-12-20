using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Widget;

namespace Microsoft.Xna.Framework.Input
{
    public static partial class KeyboardInput
    {
        private static TaskCompletionSource<string> tcs;
        private static AlertDialog alert;

        private static Task<string> PlatformShow(string title, string description, string defaultText, bool usePasswordMode)
        {
            tcs = new TaskCompletionSource<string>();

            Game.Activity.RunOnUiThread(() =>
            {
                alert = new AlertDialog.Builder(Game.Activity).Create();

                alert.SetTitle(title);
                alert.SetMessage(description);

                var input = new EditText(Game.Activity) { Text = defaultText };

                if (!string.IsNullOrEmpty(defaultText))
                    input.SetSelection(defaultText.Length);

                input.SetSelectAllOnFocus(true);

                if (usePasswordMode)
                    input.InputType = Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextVariationPassword;

                alert.SetView(input);

                alert.SetButton((int)DialogButtonType.Positive, "Ok", (sender, args) =>
                {
                    if (!tcs.Task.IsCompleted)
                        tcs.SetResult(input.Text);
                });

                alert.SetButton((int)DialogButtonType.Negative, "Cancel", (sender, args) =>
                {
                    if (!tcs.Task.IsCompleted)
                        tcs.SetResult(defaultText);
                });

                alert.CancelEvent += (sender, args) =>
                {
                    if (!tcs.Task.IsCompleted)
                        tcs.SetResult(defaultText);
                };

                alert.Show();
            });

            return tcs.Task;
        }

        private static void PlatformCancel(string result)
        {
            alert.Dismiss();
            tcs.SetResult(result);
        }
    }
}
