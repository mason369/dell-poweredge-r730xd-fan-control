using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DellR730xdFanControlCenter;

public static class Localization
{
    public static readonly DependencyProperty KeyProperty =
        DependencyProperty.RegisterAttached(
            "Key",
            typeof(string),
            typeof(Localization),
            new PropertyMetadata(null));

    public static string? GetKey(DependencyObject obj)
    {
        return (string?)obj.GetValue(KeyProperty);
    }

    public static void SetKey(DependencyObject obj, string? value)
    {
        obj.SetValue(KeyProperty, value);
    }

    public static void Apply(DependencyObject root)
    {
        ApplyElement(root);
    }

    private static void ApplyElement(DependencyObject element)
    {
        if (element is FrameworkElement frameworkElement)
        {
            var key = GetKey(frameworkElement);
            if (!string.IsNullOrWhiteSpace(key))
            {
                ApplyString(frameworkElement, key);
            }
        }

        if (element is NavigationView navigationView)
        {
            foreach (var item in navigationView.MenuItems.OfType<DependencyObject>())
            {
                ApplyElement(item);
            }

            foreach (var item in navigationView.FooterMenuItems.OfType<DependencyObject>())
            {
                ApplyElement(item);
            }
        }

        if (element is ItemsControl itemsControl)
        {
            foreach (var item in itemsControl.Items.OfType<DependencyObject>())
            {
                ApplyElement(item);
            }
        }

        var childCount = VisualTreeHelper.GetChildrenCount(element);
        for (var index = 0; index < childCount; index++)
        {
            ApplyElement(VisualTreeHelper.GetChild(element, index));
        }
    }

    private static void ApplyString(FrameworkElement element, string key)
    {
        var value = LocalizationService.T(key);

        switch (element)
        {
            case TextBlock textBlock:
                textBlock.Text = value;
                break;
            case AppBarButton appBarButton:
                appBarButton.Label = value;
                break;
            case NavigationViewItem navigationViewItem:
                navigationViewItem.Content = value;
                break;
            case ToggleSwitch toggleSwitch:
                toggleSwitch.Header = value;
                break;
            case CheckBox checkBox:
                checkBox.Content = value;
                break;
            case NumberBox numberBox:
                numberBox.Header = value;
                break;
            case TextBox textBox:
                textBox.Header = value;
                break;
            case PasswordBox passwordBox:
                passwordBox.Header = value;
                break;
            case ComboBox comboBox:
                comboBox.Header = value;
                break;
            case ComboBoxItem comboBoxItem:
                comboBoxItem.Content = value;
                break;
            case InfoBar infoBar:
                infoBar.Message = value;
                break;
            case Button button when button.Content is string:
                button.Content = value;
                break;
            default:
                throw new InvalidOperationException($"本地化键 {key} 无法应用到控件类型 {element.GetType().Name}。");
        }
    }
}
