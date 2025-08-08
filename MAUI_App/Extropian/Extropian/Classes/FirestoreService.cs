using Plugin.CloudFirestore;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;

namespace Extropian.Classes;

public static class FirestoreService
{



    public static async Task<List<SessionData>> GetAllSessionsForUser(string userId, Page page)
    {
        var allSessions = new List<SessionData>();

        try
        {
            var documents = await CrossCloudFirestore.Current
                .Instance
                .Collection("ExtropianApp")
                .Document("Gen1p5")
                .Collection("User_Swing_Metrics")
                .Document(userId)
                .Collection("Session_Data")
                .GetAsync();

            foreach (var doc in documents.Documents)
            {
                string sessionTimestamp = doc.Id;
                //await page.DisplayAlert(" Session", $"ID: {sessionTimestamp}", "OK");

                var session = new SessionData
                {
                    SessionTimestamp = sessionTimestamp,
                    Swings = new List<SwingData>()
                };

                foreach (var kvp in doc.Data)
                {
                    string swingTimestamp = kvp.Key;
                    object value = kvp.Value;

                    if (value == null)
                    {
                        continue;
                    }


                    IDictionary<string, object> metrics = null;

                    if (value is IDictionary<string, object> directDict)
                    {
                        metrics = directDict;
                    }
                    else if (value is DocumentObject docObj && docObj.Value is IDictionary<string, object> wrappedDict)
                    {
                        metrics = wrappedDict;
                    }

                    if (metrics == null)
                    {
                        continue;
                    }

                    if (!metrics.TryGetValue("wrist_speed", out var wristSpeedObj) ||
                        !metrics.TryGetValue("hip_rotation", out var hipRotationObj) ||
                        !metrics.TryGetValue("wrist_init_ts", out var wristInitTSObj) ||
                        !metrics.TryGetValue("hip_init_ts", out var hipInitTsObj) ||
                        !metrics.TryGetValue("torso_init_ts", out var torsoInitTsObj) 
                        )
                    {
                        continue;
                    }

                    try
                    {
                        var swing = new SwingData
                        {
                            TimestampString = swingTimestamp,
                            WristSpeed = Convert.ToDouble(wristSpeedObj),
                            HipRotation = Convert.ToInt32(hipRotationObj),
                            WristInitTS = Convert.ToInt32(wristInitTSObj),
                            HipInitTS = Convert.ToInt32(hipInitTsObj),
                            TorsoInitTS = Convert.ToInt32(torsoInitTsObj)
                        };

                        session.Swings.Add(swing);
                    }
                    catch (Exception ex)
                    {
                        await page.DisplayAlert(" Parse Error", ex.Message, "OK");
                    }
                }

                allSessions.Add(session);
            }

            return allSessions;
        }
        catch (Exception ex)
        {
            await page.DisplayAlert(" Exception", ex.Message, "OK");
            return allSessions;
        }
    }







}
