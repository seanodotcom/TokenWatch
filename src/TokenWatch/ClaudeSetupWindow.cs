using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TokenWatch;

internal enum ClaudeSetupChoice { Cancel, Install, Guide }
internal sealed class ClaudeSetupWindow : Window
{
    internal ClaudeSetupChoice Choice { get; private set; }
    internal ClaudeSetupWindow()
    {
        Title = "Connect your Claude account"; Width = 460; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(32, 41, 55)); ShowInTaskbar = false;
        var panel = new StackPanel { Margin = new Thickness(24) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = "Connect your Claude account", FontSize = 21, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) });
        panel.Children.Add(new TextBlock { Text = "TokenWatch couldn't find a compatible Claude Code sign-in helper. Set up Anthropic's official Windows helper, then sign in with your existing Claude account using Anthropic's own window and browser. Setup opens a separate window.", TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0, 0, 0, 20) });
        var buttons = new WrapPanel(); panel.Children.Add(buttons);
        Add("Set up and sign in", ClaudeSetupChoice.Install); Add("Setup guide", ClaudeSetupChoice.Guide); Add("Cancel", ClaudeSetupChoice.Cancel);
        void Add(string text, ClaudeSetupChoice choice)
        {
            var button = new Button { Content = text, Style = (Style)Application.Current.FindResource("ActionButton"), Margin = new Thickness(0, 0, 8, 8), IsCancel = choice == ClaudeSetupChoice.Cancel };
            button.Click += (_, _) => { Choice = choice; DialogResult = choice == ClaudeSetupChoice.Install; };
            buttons.Children.Add(button);
        }
    }
}
