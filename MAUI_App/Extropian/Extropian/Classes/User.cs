using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Extropian.Classes;

static internal class User
{
    public static string userId { get; set; }
    public static string password { get; set; }
    public static int age { get; set; }
    public static int height { get; set; }
    public static int weight { get; set; }
    public static string gender { get; set; }
    public static string handedness { get; set; }
    public static int handicap { get; set; }
    public static string ethnicity { get; set; }
    public static int skillLevel { get; set; }

    public static void UpdateUser(Dictionary<string, object> userDict)
    {
        User.userId = userDict["UserId"].ToString();
        User.password = userDict["Password"].ToString();
        User.age = int.Parse(userDict["Age"].ToString());
        User.height = int.Parse(userDict["Height"].ToString());
        User.weight = int.Parse(userDict["Weight"].ToString());
        User.gender = userDict["Gender"].ToString();
        User.ethnicity = userDict["Ethnicity"].ToString();
        User.handedness = userDict["Handedness"].ToString();
        User.skillLevel = int.Parse(userDict["SkillLevel"].ToString());
    }

    public static Dictionary<string, object> GetUserData()
    {
        Dictionary<string, object> userDict = new Dictionary<string, object>
        {
            { "UserId", User.userId },
            { "Password", User.password },
            { "Age", User.age },
            { "Gender", User.gender },
            { "Height", User.height },
            { "Weight", User.weight },
            { "Handedness", User.handedness },
            { "Ethnicity", User.ethnicity },
            { "Handicap", User.handicap },
            { "SkillLevel", User.skillLevel }
        };

        return userDict;
    }

    public static (string, string) GetUserCredentials()
    {
        return (User.userId, User.password);
    }

    public static void ApplyUser(string userId, string password)
    {
        User.userId = userId;
        User.password = password;
    }

    


}



