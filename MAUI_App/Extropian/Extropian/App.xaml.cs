namespace Extropian
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();

            // Set MainPage to AppShell (Shell handles all navigation)
            MainPage = new AppShell();
        }
    }

}