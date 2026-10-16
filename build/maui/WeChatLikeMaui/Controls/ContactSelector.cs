namespace WeChatLikeMaui.Controls;

/// <summary>Native, searchable contact choices. Names stay selected when the search changes.</summary>
public sealed class ContactSelector : ContentView
{
    private readonly SortedSet<string> _names = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly HashSet<string> _selected = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly Entry _manual = new() { Placeholder = "输入微信显示名；多个名字用逗号分隔" };
    private readonly SearchBar _search = new() { Placeholder = "搜索候选用户", BackgroundColor = Colors.Transparent };
    private readonly VerticalStackLayout _rows = new() { Spacing = 2 };
    private readonly Label _summary = new() { FontSize = 12, TextColor = Color.FromArgb("#5E756F") };
    public event EventHandler? SelectionChanged;

    public ContactSelector()
    {
        var add = new Button { Text = "添加", Style = (Style)Application.Current!.Resources["SecondaryButton"] };
        add.Clicked += (_, _) => AddManual();
        _manual.Completed += (_, _) => AddManual();
        _search.TextChanged += (_, _) => Render();
        var input = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 8 };
        input.Add(_manual); input.Add(add, 1);
        Content = new VerticalStackLayout
        {
            Spacing = 8,
            Children = { input, _search, new ScrollView { MaximumHeightRequest = 240, Content = _rows }, _summary }
        };
        Render();
    }

    public string[] SelectedNames => _selected.Order(StringComparer.CurrentCultureIgnoreCase).ToArray();
    public void SetSelection(IEnumerable<string> names)
    {
        _selected.Clear();
        foreach (var name in names.Select(n => n.Trim()).Where(n => n.Length > 0)) { _names.Add(name); _selected.Add(name); }
        Render();
    }
    public void AddCandidates(IEnumerable<string> names)
    {
        foreach (var name in names.Select(n => n.Trim()).Where(n => n.Length > 0)) _names.Add(name);
        Render();
    }
    public void SetAutomationPrefix(string prefix)
    {
        _manual.AutomationId = prefix + "AddEntry";
        _search.AutomationId = prefix + "Search";
        _summary.AutomationId = prefix + "Summary";
    }
    private void AddManual()
    {
        foreach (var name in (_manual.Text ?? "").Split([',', '，', ';', '；', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        { _names.Add(name); _selected.Add(name); }
        _manual.Text = "";
        Render();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    private void Render()
    {
        _rows.Children.Clear();
        var matching = _names.Where(n => n.Contains(_search.Text?.Trim() ?? "", StringComparison.CurrentCultureIgnoreCase)).ToList();
        foreach (var name in matching.Take(200))
        {
            var check = new CheckBox { IsChecked = _selected.Contains(name), Color = Color.FromArgb("#11856B"), WidthRequest = 44, HeightRequest = 44, MinimumWidthRequest = 0, MinimumHeightRequest = 0, HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Center };
            var label = new Label { Text = name, VerticalOptions = LayoutOptions.Center };
            SemanticProperties.SetDescription(check, name);
            check.CheckedChanged += (_, e) =>
            {
                if (e.Value) _selected.Add(name); else _selected.Remove(name);
                UpdateSummary();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            };
            var row = new Grid { MinimumHeightRequest = 48, ColumnSpacing = 8, ColumnDefinitions = { new ColumnDefinition(new GridLength(48)), new ColumnDefinition(GridLength.Star) } };
            row.Add(check); row.Add(label, 1);
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => check.IsChecked = !check.IsChecked;
            label.GestureRecognizers.Add(tap);
            _rows.Children.Add(row);
        }
        if (matching.Count == 0) _rows.Children.Add(new Label { Text = _names.Count == 0 ? "还没有候选用户。可手动添加，或从已登录的微信读取通讯录。" : "没有匹配用户；已选项会保留。", FontSize = 12, TextColor = Color.FromArgb("#5E756F") });
        if (matching.Count > 200) _rows.Children.Add(new Label { Text = "仅显示前 200 位，请用搜索缩小范围。", FontSize = 12 });
        UpdateSummary();
    }
    private void UpdateSummary() => _summary.Text = $"已选 {_selected.Count} 位" + (_selected.Count > 0 ? "：" + string.Join("、", SelectedNames) : "；空列表不会选择所有人。");
}
