using Extropian.Classes;
using Extropian.ContentViews;
using System.Text;

namespace Extropian.Pages;

public partial class Dashboard : ContentPage
{
    public Dashboard()
    {
        InitializeComponent();
        LoadSessionCards();
    }

    private async void LoadSessionCards()
    {
        var sessions = await FirestoreService.GetAllSessionsForUser("def456", this); // Pass 'this' for alerts

        await DisplayAlert("Session Count", sessions.Count.ToString(), "OK");

        foreach (var session in sessions)
        {
            var sb = new StringBuilder();
            foreach (var swing in session.Swings)
            {
                sb.AppendLine($"{swing.TimestampString}  Speed: {swing.WristSpeed}  Rotation: {swing.HipRotation}  WristTS:{swing.WristInitTS}  HipTS:{swing.HipInitTS}  TorsoTS:{swing.TorsoInitTS}");
            }

            var card = new DashboardCard
            {
                Title = session.SessionTimestamp,
                Description = sb.ToString(),
                ImageSource = "session.png"
            };

            card.Tapped += OnCardTapped;
            CardContainer.Children.Add(card);
        }
    }

    private void OnCardTapped(object sender, EventArgs e)
    {
        if (sender is DashboardCard card)
        {
            DisplayAlert("Card tapped", card.Title, "OK");
        }
    }
}
