namespace Extropian.ContentViews;

public partial class DashboardCard : ContentView
{
    public DashboardCard()
    {
        InitializeComponent();

        // Add tap gesture support to the entire card
        var tapGesture = new TapGestureRecognizer();
        tapGesture.Tapped += (s, e) => Tapped?.Invoke(this, EventArgs.Empty);
        this.GestureRecognizers.Add(tapGesture);
    }

    // Events
    public event EventHandler Tapped;

    // Bindable Properties

    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(DashboardCard), string.Empty, propertyChanged: OnTitleChanged);

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    private static void OnTitleChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is DashboardCard card && newValue is string newTitle)
            card.TitleLabel.Text = newTitle;
    }

    public static readonly BindableProperty DescriptionProperty =
        BindableProperty.Create(nameof(Description), typeof(string), typeof(DashboardCard), string.Empty, propertyChanged: OnDescriptionChanged);

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    private static void OnDescriptionChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is DashboardCard card && newValue is string newDesc)
            card.DescriptionLabel.Text = newDesc;
    }

    public static readonly BindableProperty ImageSourceProperty =
        BindableProperty.Create(nameof(ImageSource), typeof(ImageSource), typeof(DashboardCard), null, propertyChanged: OnImageSourceChanged);

    public ImageSource ImageSource
    {
        get => (ImageSource)GetValue(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }

    private static void OnImageSourceChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is DashboardCard card && newValue is ImageSource newSrc)
            card.CardImage.Source = newSrc;
    }
}
