using Avalonia;
using Avalonia.Controls;
using Knossos.NET.Classes;
using Knossos.NET.ViewModels;
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Knossos.NET.Views
{
    public partial class NebulaModListView : UserControl
    {
        public static NebulaModListView? Instance;
        private bool filterButtonsGenerated = false;
        private StackPanel? sortPanel;
        private WrapPanel? filterPanel;

        public NebulaModListView()
        {
            InitializeComponent();
            Instance = this;
            AttachButtonUpdate();
        }

        private void AttachButtonUpdate()
        {
            try
            {
                sortPanel = this.FindControl<StackPanel>("SortPanel");
                filterPanel = this.FindControl<WrapPanel>("FilterPanel");
                var filterFlyout = this.FindControl<Button>("FilterFlyout");
                if (filterFlyout != null)
                {
                    filterFlyout.Click += (_, __) =>
                    {
                        if (!filterButtonsGenerated)
                            GenerateFilterButtons();
                        ApplySortButtonsStyle();
                        ApplyFilterButtonsStyle();
                    };
                }

                if (sortPanel != null)
                {
                    foreach (var item in sortPanel.Children)
                    {
                        if (item is Button button)
                        {
                            button.Click += async (_, __) =>
                            {
                                //Change colors when clicked, wait until after the active sort was saved
                                await Task.Delay(100);
                                ApplySortButtonsStyle();
                            };
                        }
                    }
                }
            }catch(Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "NebulaModListView.AttachButtonUpdate()", ex);
            }
        }

        private void ApplyFilterButtonsStyle()
        {
            try
            {
                if (filterPanel != null && MainViewModel.Instance != null)
                {
                    if (MainViewModel.Instance.tagFilter.Any())
                    {
                        var tags = ModTags.GetListAllFilters();
                        foreach (var item in filterPanel.Children)
                        {
                            if (item is Button button && button.Tag is int tagIndex)
                            {
                                if (tags.Count() > tagIndex && MainViewModel.Instance.tagFilter.Contains(tags[tagIndex]))
                                {
                                    button.Classes.Add("Secondary");
                                }
                                else
                                {
                                    button.Classes.Remove("Secondary");
                                }
                            }
                        }
                    }
                    else
                    {
                        foreach (var item in filterPanel.Children)
                        {
                            if (item is Button button)
                                button.Classes.Remove("Secondary");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "NebulaModListView.ApplyFilterButtonsStyle()", ex);
            }
        }

        private void ApplySortButtonsStyle()
        {
            try
            {
                if (sortPanel != null)
                {
                    foreach (var item in sortPanel.Children)
                    {
                        if (item is Button button)
                        {
                            if (button.CommandParameter != null && (string)button.CommandParameter == Knossos.globalSettings.sortType.ToString())
                            {
                                button.Classes.Add("Secondary");
                            }
                            else
                            {
                                button.Classes.Remove("Secondary");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "NebulaModListView.ApplySortButtonsStyle()", ex);
            }
        }

        public void GenerateFilterButtons()
        {
            try
            {
                filterButtonsGenerated = true;
                if (filterPanel != null)
                {
                    filterPanel.Children.Clear();
                    var tags = ModTags.GetListAllFilters();
                    if (tags != null && tags.Any())
                    {
                        foreach (var tag in tags.Select((x, i) => new { Value = x, Index = i }))
                        {
                            TextInfo myTI = new CultureInfo("en-US", false).TextInfo;
                            var displayName = String.Empty;

                            if (Knossos.filterDisplayStrings.ContainsKey(tag.Value.ToLower()))
                            {
                                displayName = Knossos.filterDisplayStrings[tag.Value.ToLower()];
                            } else {
                                displayName = myTI.ToTitleCase(tag.Value.Replace("_", " "));
                            }
                            
                            var button = new Button { Content = displayName, Tag = tag.Index, Width = 150, Margin = new Thickness(2) };
                            button.Click += (_, __) =>
                            {
                                //This code runs when the button is clicked
                                if (button.Tag is int tagIndex && this.DataContext is NebulaModListViewModel dt)
                                {
                                    if (!button.Classes.Contains("Secondary"))
                                    {
                                        dt.ApplyTagFilter(tagIndex);
                                        button.Classes.Add("Secondary");
                                    }
                                    else
                                    {
                                        dt.RemoveTagFilter(tagIndex);
                                        button.Classes.Remove("Secondary");
                                    }
                                }
                            };
                            filterPanel.Children.Add(button);
                        }
                    }
                }
                else
                {
                    Log.Add(Log.LogSeverity.Error, "NebulaModListView.GenerateFilterButtons()", "Unable to find FilterPanel to generate filter buttons");
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "NebulaModListView.GenerateFilterButtons()", ex);
            }
        }
    }
}
