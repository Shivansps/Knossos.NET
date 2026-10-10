using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Knossos.NET.Converters;
using Knossos.NET.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Knossos.NET.Views;

public partial class CustomHomeView : UserControl
{
    internal List<string> buttonUrls = new List<string>();

    public CustomHomeView()
    {
        InitializeComponent();

        //Generate Link Buttons
        try
        {
            if (CustomLauncher.HomeLinkButtons != null && CustomLauncher.HomeLinkButtons.Any())
            {
                var buttonPanel = this.FindControl<WrapPanel>("LinkButtons");
                if (buttonPanel != null)
                {
                    int index = 0;
                    foreach (var b in CustomLauncher.HomeLinkButtons)
                    {
                        var linkButton = new Button { Tag = index, Name = b.ToolTip };
                        if (b.IconPath != null)
                        {
                            var converter = new BitmapAssetValueConverter();
                            var bitmap = converter.Convert(b.IconPath, typeof(Bitmap), null, null);
                            if(bitmap != null)
                            {
                                linkButton.Content = new Image { Source = (Bitmap)bitmap, Width = 30, Height = 30 };
                            }
                        }

                        linkButton.Click += (_, __) =>
                        {
                            //This code runs when the button is clicked
                            try
                            {
                                if(linkButton.Tag != null)
                                {
                                    var url = buttonUrls[(int)linkButton.Tag];
                                    KnUtils.OpenBrowserURL(url);
                                }    
                            }
                            catch (Exception ex)
                            {
                                Log.Add(Log.LogSeverity.Error, "CustomHomeView.Constructor(LinkButtonClick)", ex);
                            }
                        };

                        buttonPanel.Children.Add(linkButton);
                        index++;
                        buttonUrls.Add(b.LinkURL);
                        ToolTip.SetTip(linkButton, b.ToolTip);
                    }
                }
                else
                {
                    Log.Add(Log.LogSeverity.Error, "CustomHomeView.Constructor()", "Unable to find LinkButtons panel.");
                }
            }
        }
        catch(Exception ex)
        {
            Log.Add(Log.LogSeverity.Error, "CustomHomeView.Constructor()", ex);
        }

        //Home Buttons Customizations
        try
        {
            if (CustomLauncher.HomeButtonConfigs != null && CustomLauncher.HomeButtonConfigs.Any())
            {
                foreach(var config in CustomLauncher.HomeButtonConfigs)
                {
                    if (config.ButtonID == null)
                        continue;
                    var button = this.FindControl<Button>(config.ButtonID!);
                    if (button != null)
                    {
                        if (config.DisplayText != null && config.ButtonID != "ButtonLaunchAdvanced")
                            button.Content = config.DisplayText;
                        if (config.ToolTip != null)
                            ToolTip.SetTip(button, config.ToolTip);
                        if (config.FontSize != null)
                            button.FontSize = config.FontSize.Value;
                        if (config.Width != null)
                            button.Width = config.Width.Value;
                        if (config.Height != null)
                            button.Height = config.Height.Value;
                        if (config.BackgroundHexColor != null)
                            button.Background = SolidColorBrush.Parse(config.BackgroundHexColor);
                        if (config.ForegroundHexColor != null)
                            button.Foreground = SolidColorBrush.Parse(config.ForegroundHexColor);
                        if (config.BorderHexColor != null)
                            button.BorderBrush = SolidColorBrush.Parse(config.BorderHexColor);

                        if (config.MouseOverBackgroundHexColor != null || config.MouseOverForegroundHexColor != null || config.MouseOverBorderHexColor != null)
                        {
                            Style style = new Style(x => x.OfType<Button>().Class(":pointerover").Template().OfType<ContentPresenter>());
                            if(config.MouseOverBackgroundHexColor != null)
                            {
                                style.Setters.Add(new Setter(Button.BackgroundProperty, SolidColorBrush.Parse(config.MouseOverBackgroundHexColor)));
                            }
                            if(config.MouseOverForegroundHexColor != null)
                            {
                                style.Setters.Add(new Setter(Button.ForegroundProperty, SolidColorBrush.Parse(config.MouseOverForegroundHexColor)));
                            }
                            if (config.MouseOverBorderHexColor != null)
                            {
                                style.Setters.Add(new Setter(Button.BorderBrushProperty, SolidColorBrush.Parse(config.MouseOverBorderHexColor)));
                            }
                            button.Styles.Add(style);
                        }
                    }
                    else
                    {
                        Log.Add(Log.LogSeverity.Error, "CustomHomeView.Constructor()", "Unable to find home button id: " + config.ButtonID + " to apply customizations.");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Add(Log.LogSeverity.Error, "CustomHomeView.Constructor()", ex);
        }
    }
}