using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using pdfMerge.Services;

namespace pdfMerge.Views
{
    public partial class PasswordPromptWindow : Window
    {
        private readonly string _filePath;
        private bool _isPasswordVisible = false;
        private bool _isUpdatingInternally = false;

        public string EnteredPassword { get; private set; } = string.Empty;

        public PasswordPromptWindow(string filePath)
        {
            InitializeComponent();
            _filePath = filePath;

            string fileName = Path.GetFileName(filePath);
            TxtFileName.Text = fileName;
            TxtFileName.ToolTip = filePath;

            Loaded += (s, e) =>
            {
                PwdBox.Focus();
            };
        }

        private void BtnToggleVisibility_Click(object sender, RoutedEventArgs e)
        {
            _isPasswordVisible = !_isPasswordVisible;

            if (_isPasswordVisible)
            {
                TxtPlainPassword.Text = PwdBox.Password;
                PwdBox.Visibility = Visibility.Collapsed;
                TxtPlainPassword.Visibility = Visibility.Visible;
                TxtToggleIcon.Text = "🙈";
                TxtPlainPassword.Focus();
                TxtPlainPassword.CaretIndex = TxtPlainPassword.Text.Length;
            }
            else
            {
                PwdBox.Password = TxtPlainPassword.Text;
                TxtPlainPassword.Visibility = Visibility.Collapsed;
                PwdBox.Visibility = Visibility.Visible;
                TxtToggleIcon.Text = "👁️";
                PwdBox.Focus();
            }
        }

        private void PwdBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingInternally) return;

            if (TxtError.Visibility == Visibility.Visible)
            {
                TxtError.Visibility = Visibility.Collapsed;
            }
        }

        private void TxtPlainPassword_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_isUpdatingInternally) return;

            if (TxtError.Visibility == Visibility.Visible)
            {
                TxtError.Visibility = Visibility.Collapsed;
            }
        }

        private void Input_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnUnlock_Click(sender, e);
                e.Handled = true;
            }
        }

        private async void BtnUnlock_Click(object sender, RoutedEventArgs e)
        {
            string password = _isPasswordVisible ? TxtPlainPassword.Text : PwdBox.Password;

            if (string.IsNullOrEmpty(password))
            {
                TxtError.Text = "Please enter the password.";
                TxtError.Visibility = Visibility.Visible;
                FocusActiveInput();
                return;
            }

            BtnUnlock.IsEnabled = false;
            TxtUnlockBtnLabel.Text = "Verifying...";
            TxtError.Visibility = Visibility.Collapsed;

            try
            {
                var result = await PdfSecurityService.VerifyPasswordAsync(_filePath, password);

                if (result.Success)
                {
                    PdfSecurityService.SetPassword(_filePath, password);
                    EnteredPassword = password;
                    DialogResult = true;
                    Close();
                }
                else
                {
                    TxtError.Text = result.ErrorMessage ?? "Incorrect password. Please try again.";
                    TxtError.Visibility = Visibility.Visible;
                    FocusActiveInput();
                }
            }
            catch (Exception ex)
            {
                TxtError.Text = $"Error verifying password: {ex.Message}";
                TxtError.Visibility = Visibility.Visible;
                FocusActiveInput();
            }
            finally
            {
                BtnUnlock.IsEnabled = true;
                TxtUnlockBtnLabel.Text = "Unlock PDF";
            }
        }

        private void FocusActiveInput()
        {
            if (_isPasswordVisible)
            {
                TxtPlainPassword.Focus();
                TxtPlainPassword.SelectAll();
            }
            else
            {
                PwdBox.Focus();
                PwdBox.SelectAll();
            }
        }

        private void BtnSkip_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
