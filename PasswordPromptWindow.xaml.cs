using System.Windows;
using System.Windows.Media.Animation;

namespace Elektra.UI;

public partial class PasswordPromptWindow : Window
{
    private readonly bool _confirmPassword;

    public string? Password { get; private set; }

    public PasswordPromptWindow(string folderName, bool encrypting, bool confirmPassword)
    {
        InitializeComponent();
        _confirmPassword = confirmPassword;
        FolderText.Text = $"{(encrypting ? "Encrypt" : "Decrypt")} {folderName}";
        WarningText.Text = encrypting
            ? "WARNING: This replaces each regular file in this folder and its subfolders with an authenticated .elektra file after writing it safely. Keep your password safe; Elektra does not store it. Deleting originals cannot securely erase old disk sectors, backups, or cloud copies."
            : "WARNING: This restores files from .elektra format. Existing files will never be overwritten. Use the same password used to encrypt them; Elektra does not store it.";
        ConfirmLabel.Visibility = confirmPassword ? Visibility.Visible : Visibility.Collapsed;
        ConfirmInput.Visibility = confirmPassword ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => PasswordInput.Focus();
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        bool passwordLongEnough = PasswordInput.Password.Length >= 8;
        bool confirmationMatches = !_confirmPassword ||
            (ConfirmInput.Password.Length >= 8 && string.Equals(PasswordInput.Password, ConfirmInput.Password, StringComparison.Ordinal));
        ContinueButton.IsEnabled = passwordLongEnough && confirmationMatches;
        StatusText.Text = !passwordLongEnough && PasswordInput.Password.Length > 0
            ? "Use at least eight characters."
            : _confirmPassword && ConfirmInput.Password.Length > 0 && !confirmationMatches
                ? "The passwords do not match."
                : string.Empty;

        var flash = new DoubleAnimation(0.9, 0, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        LightningB.BeginAnimation(OpacityProperty, flash, HandoffBehavior.SnapshotAndReplace);
        LightningA.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(180)) { AutoReverse = true },
            HandoffBehavior.SnapshotAndReplace);
    }

    private void OnContinueClicked(object sender, RoutedEventArgs e)
    {
        if (PasswordInput.Password.Length < 8)
        {
            StatusText.Text = "Use at least eight characters.";
            return;
        }

        if (_confirmPassword && !string.Equals(PasswordInput.Password, ConfirmInput.Password, StringComparison.Ordinal))
        {
            StatusText.Text = "The passwords do not match. Please try again.";
            ConfirmInput.Clear();
            ConfirmInput.Focus();
            return;
        }

        Password = PasswordInput.Password;
        PasswordInput.Clear();
        ConfirmInput.Clear();
        DialogResult = true;
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        PasswordInput.Clear();
        ConfirmInput.Clear();
        DialogResult = false;
    }
}
