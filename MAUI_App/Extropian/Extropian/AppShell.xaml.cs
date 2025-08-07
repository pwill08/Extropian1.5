using Extropian.Pages;

namespace Extropian
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            // Register routes
            //Routing.RegisterRoute(nameof(Dashboard), typeof(Dashboard));
            Routing.RegisterRoute(nameof(SignUp), typeof(SignUp));
        }
    }
}
