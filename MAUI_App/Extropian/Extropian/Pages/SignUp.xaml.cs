using Extropian.Classes;
using System.Reflection.Metadata;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Extropian.Pages;

public partial class SignUp : ContentPage
{
	public SignUp()
	{
		InitializeComponent();
	}



    private async void OnSignUp_ButtonClicked(object sender, EventArgs e)
    {
        await CheckSignUpForms();
    }

    private async Task CheckSignUpForms()
    {
        string userId = UserIdEntry.Text?.Trim() ?? string.Empty;
        string password_1 = PasswordEntry.Text?.Trim() ?? string.Empty;
        string password_2 = ReEnterPasswordEntry.Text?.Trim() ?? string.Empty;
        string ageText = AgeEntry.Text?.Trim() ?? string.Empty;
        string heightText = HeightEntry.Text?.Trim() ?? string.Empty;
        string weightText = WeightEntry.Text?.Trim() ?? string.Empty;
        string handicapText = HandicapEntry.Text?.Trim() ?? string.Empty;

        string gender = null;
        if (GenderMaleRadio.IsChecked)
            gender = "Male";
        else if (GenderFemaleRadio.IsChecked)
            gender = "Female";

        string ethnicity = EthnicityPicker.SelectedItem?.ToString();

        string handedness = null;
        if (RightHandedRadio.IsChecked)
            handedness = "Right";
        else if (LeftHandedRadio.IsChecked)
            handedness = "Left";

        int skillLevel = (int)SkillLevelSlider.Value;


        if (password_1 != password_2)
        {
            await DisplayAlert("Error", "Passwords do not match.", "OK");
            return;
        }

        // Collect missing fields
        List<string> missingFields = new List<string>();

        if (string.IsNullOrWhiteSpace(userId)) missingFields.Add("User ID");
        if (!int.TryParse(ageText, out int age)) missingFields.Add("Valid Age");
        if (string.IsNullOrWhiteSpace(gender)) missingFields.Add("Gender");
        if (!int.TryParse(heightText, out int height)) missingFields.Add("Valid Age");
        if (!int.TryParse(weightText, out int weight)) missingFields.Add("Valid Age");
        if (string.IsNullOrWhiteSpace(handedness)) missingFields.Add("Handedness");
        if (string.IsNullOrWhiteSpace(ethnicity)) missingFields.Add("Ethnicity");
        if (!int.TryParse(handicapText, out int handicap)) missingFields.Add("Valid Age");

        if (missingFields.Count > 0)
        {
            string message = "\n- " + string.Join("\n- ", missingFields);
            await DisplayAlert("Missing Fields", message, "OK");
            return;
        }

        // All fields are valid – continue with sign-up logic...
        Dictionary<string, object> userDict = new Dictionary<string, object>
        {
            { "UserId", userId },
            { "Password", password_1 },
            { "Age", age },
            { "Gender", gender },
            { "Height", height },
            { "Weight", weight },
            { "Handedness", handedness },
            { "Ethnicity", ethnicity },
            { "Handicap", handicap },
            { "SkillLevel", skillLevel }
        };

        ApplyUser(userDict);
        await DisplayAlert("", "Successfully Signed Up", "OK");
        await Navigation.PopAsync();

    }

    public void ApplyUser(Dictionary<string, object> userDict)
    {
        User.UpdateUser(userDict);
        
    }



    private void EntryField_ParseSpaces(object sender, TextChangedEventArgs e)
    {
        if (e.NewTextValue.Contains(" "))
        {
            UserIdEntry.Text = e.NewTextValue.Replace(" ", "");
        }
    }



    private void OnSkillLevelChanged(object sender, ValueChangedEventArgs e)
    {
        // Snap to whole number
        int roundedValue = (int)Math.Round(e.NewValue);
        SkillLevelSlider.Value = roundedValue;  // update actual slider value
        SkillLevelSliderValue.Text = roundedValue.ToString(); // update label
    }




}