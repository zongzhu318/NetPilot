using System.Xml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;

namespace NetPilot.App.Views.Controls;

public partial class CodeEditor : UserControl
{
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<CodeEditor, string?>(
        nameof(Text), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(IsReadOnly));

    public static readonly StyledProperty<string?> SyntaxProperty =
        AvaloniaProperty.Register<CodeEditor, string?>(nameof(Syntax));

    public static readonly StyledProperty<bool> WordWrapProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(WordWrap));

    private static readonly Lazy<IHighlightingDefinition?> JsonDefinition = new(LoadJsonDefinition);
    private AvaloniaEdit.TextEditor? _editor;
    private bool _syncing;

    public CodeEditor()
    {
        InitializeComponent();
        _editor = this.FindControl<AvaloniaEdit.TextEditor>("Editor")
            ?? throw new InvalidOperationException("CodeEditor 缺少 Editor 控件。");
        _editor.TextChanged += OnEditorTextChanged;
        _editor.IsReadOnly = IsReadOnly;
        _editor.WordWrap = WordWrap;
        ApplyText();
        ApplySyntax();
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public string? Syntax
    {
        get => GetValue(SyntaxProperty);
        set => SetValue(SyntaxProperty, value);
    }

    public bool WordWrap
    {
        get => GetValue(WordWrapProperty);
        set => SetValue(WordWrapProperty, value);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_editor is null)
        {
            return;
        }

        if (change.Property == TextProperty)
        {
            ApplyText();
        }
        else if (change.Property == IsReadOnlyProperty)
        {
            _editor.IsReadOnly = IsReadOnly;
        }
        else if (change.Property == SyntaxProperty)
        {
            ApplySyntax();
        }
        else if (change.Property == WordWrapProperty)
        {
            _editor.WordWrap = WordWrap;
        }
    }

    private void ApplyText()
    {
        if (_syncing || _editor is null)
        {
            return;
        }

        _syncing = true;
        _editor.Text = Text ?? string.Empty;
        _syncing = false;
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_syncing || _editor is null)
        {
            return;
        }

        _syncing = true;
        Text = _editor.Text;
        _syncing = false;
    }

    private void ApplySyntax()
    {
        if (_editor is null)
        {
            return;
        }

        _editor.SyntaxHighlighting = string.Equals(Syntax, "Json", StringComparison.OrdinalIgnoreCase)
            ? JsonDefinition.Value
            : null;
    }

    private static IHighlightingDefinition? LoadJsonDefinition()
    {
        try
        {
            using var reader = XmlReader.Create(new StringReader(JsonXshd));
            return HighlightingLoader.Load(reader, HighlightingManager.Instance);
        }
        catch
        {
            return null;
        }
    }

    private const string JsonXshd = """
        <?xml version="1.0" encoding="utf-8"?>
        <SyntaxDefinition name="Json" extensions=".json" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
          <Color name="Digits" foreground="#D97706" />
          <Color name="String" foreground="#0EA5E9" />
          <Color name="Key" foreground="#8B5CF6" />
          <Color name="TrueFalse" foreground="#DB2777" />
          <Color name="Null" foreground="#64748B" />
          <RuleSet>
            <Rule color="Key">"([^"]*)"\s*:</Rule>
            <Span color="String" multiline="false">
              <Begin>"</Begin>
              <End>"</End>
              <RuleSet>
                <Span begin="\\" end="." />
              </RuleSet>
            </Span>
            <Keywords color="TrueFalse">
              <Word>true</Word>
              <Word>false</Word>
            </Keywords>
            <Keywords color="Null">
              <Word>null</Word>
            </Keywords>
            <Rule color="Digits">\b\d+(\.\d+)?([eE][+-]?\d+)?\b</Rule>
          </RuleSet>
        </SyntaxDefinition>
        """;
}
