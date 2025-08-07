using Extropian.Classes;
using Extropian.Pages;
using Plugin.CloudFirestore;
using System.Collections.Generic;

namespace Extropian;

public partial class MainPage : ContentPage
{

    public MainPage()
    {
        InitializeComponent();
    }





    private async void OnSignUpClicked(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new SignUp());
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();


        try
        {
            //Load user Id and password from previous sessions
            string userId = Preferences.Get("userId", string.Empty);
            string password = Preferences.Get("password", string.Empty);
            UserIdEntry.Text = userId;
            PasswordEntry.Text = password;
        }
        catch { }

        if (User.userId != null)
        {
            (string userId, string password) = User.GetUserCredentials();
            UserIdEntry.Text = userId;
            PasswordEntry.Text = password;
        }
    }

    private async void OnSignInClicked(object sender, EventArgs e)
    {
        string userId = UserIdEntry.Text.ToString().Trim();
        string password = PasswordEntry.Text.ToString().Trim();



        (bool success, string password_stored) = await RetrieveUserCredentials(userId);
        if (success)
        {
            bool approved = ApproveCredentials(userId, password_stored);
        }
        else
            return;

        await ApplyUser();
        // Navigate to the tab bar root (the default route)
        await Shell.Current.GoToAsync("//dashboard");
    }


    private async Task ApplyUser()
    {
        string userId = UserIdEntry.Text.ToString().Trim();
        string password = PasswordEntry.Text.ToString().Trim();
        User.ApplyUser(userId, password);

        //Store userid and password for future sessions
        Preferences.Set("userId", userId);
        Preferences.Set("password", password);


    }


    private async Task<(bool, string)> RetrieveUserCredentials(string userId)
    {
        try
        {


            // Access the Firestore document
            var document = await CrossCloudFirestore.Current
                .Instance
                .Collection("ExtropianApp")
                .Document("Gen1p5")
                .Collection("Users")
                .Document(userId)
                .GetAsync();

            if (document.Exists)
            {
                // Access the document's data directly
                var userData = document.Data;

                // Extract fields from the dictionary
                string retrievedPassword = userData.ContainsKey("password") ? userData["password"]?.ToString() : null;

                // Update User class or UI with retrieved data
                if (retrievedPassword != null)
                {
                    return (true, retrievedPassword);
                }
                else
                {
                    await DisplayAlert("Warning", "Some user data fields are missing.", "OK");
                    return (false, null);
                }
            }
            else
            {
                // Handle case where document does not exist
                await DisplayAlert("Error", "User document def456 does not exist.", "OK");
                return (false, null);
            }
        }
        catch (Exception ex)
        {
            // Handle any errors during Firestore retrieval
            await DisplayAlert("Error", $"Failed to retrieve user data: {ex.Message}", "OK");
            return (false, null);
        }
    }

    private bool ApproveCredentials(string password, string password_stored)
    {
        if (password == password_stored)
            return true;
        else
            return false;
            

    }







}
