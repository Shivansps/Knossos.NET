using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Knossos.NET.Views;

public partial class MainView : UserControl
{
    public static MainView? instance;

    public MainView()
    {
        instance = this;
        InitializeComponent();
    }


    /// <summary>
    /// For custom mode, for setting the task buttom at the last place in the menu
    /// we need to change the margins so it is displayed properly.
    /// </summary>
    public void FixMarginButtomTasks()
    {
        var tasks = this.FindControl<TaskInfoButtonView>("TaskButtom");
        if (tasks != null)
        {
            tasks.Margin = new Thickness(9, -45, 0, 0);
        }
        var list = this.FindControl<ListBox>("ButtomList");
        if (list != null)
        {
            list.Margin = new Thickness(2, 0, -100, 0);
        }
    }
}