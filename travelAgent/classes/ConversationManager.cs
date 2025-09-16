using GenerativeAI;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

public class ConversationManager
{
    // מחלקה פרטית לניהול מצב השיחה, כדי לשמור על הקשר בין בקשות
    private class ConversationState
    {
        public string LastCity { get; set; }
        public ChatSession ChatSession { get; set; }
    }

    private readonly GenerativeModel _generativeModel;
    private readonly IMemoryCache _cache;
    private readonly WeatherService _weatherService;
    private const string CacheKey = "ConversationState"; // מפתח לשמירת מצב השיחה במטמון

    public ConversationManager(GenerativeModel generativeModel, IMemoryCache cache, WeatherService weatherService)
    {
        _generativeModel = generativeModel;
        _cache = cache;
        _weatherService = weatherService;
    }

    // --- לוגיקת זיהוי כוונה ---
    // כעת, הפונקציה מזהה רק אם מדובר בתכנון טיול או בשיחת צ'אט רגילה.
    public string ExtractIntent(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "chat";
        text = text.ToLower();

        if (text.Contains("תכנן") || text.Contains("טיול") || text.Contains("חופשה") || text.Contains("נסיעה"))
        {
            return "trip_plan";
        }

        return "chat";
    }

    // --- חילוץ שם העיר באמצעות מודל AI ---
    private async Task<string> ExtractCity(string userMessage)
    {
        var prompt = $"נתון טקסט: '{userMessage}'. חלץ מתוכו את שם העיר (שם של עיר אמיתית בלבד). החזר רק את שם העיר ללא מילים נוספות. אם לא נמצאה עיר, החזר מחרוזת ריקה.";
        var response = await _generativeModel.GenerateContentAsync(prompt);
        return response.Text?.Trim() ?? "";
    }

    // --- חילוץ משך הזמן של הטיול באמצעות מודל AI ---
    private async Task<string> ExtractTripDuration(string userMessage)
    {
        var prompt = $"נתון טקסט: '{userMessage}'. האם המשתמש מתכנן טיול של יום אחד (היום או מחר) או טיול של מספר ימים (לדוגמה, חמישה ימים, שבוע)? אם מדובר בטיול של יום אחד, החזר את המילה 'daily'. אם מדובר בטיול של מספר ימים, החזר את המילה 'weekly'. אם לא ניתן לזהות משך זמן, החזר 'weekly' כברירת מחדל.";
        var response = await _generativeModel.GenerateContentAsync(prompt);
        var result = response.Text?.Trim().ToLower();

        return result switch
        {
            "daily" => "daily",
            "weekly" => "weekly",
            _ => "weekly" // ברירת מחדל אם המודל לא הצליח לזהות
        };
    }

    // --- פונקציה לסיכום היסטוריית השיחה ---
    // כדי לחסוך באסימונים, נשתמש במודל עצמו כדי לסכם את השיחה
    private async Task<string> SummarizeChat(ChatSession chat)
    {
        var historyPrompt = "סכם את היסטוריית השיחה הקודמת בנקודות קצרות, תוך התמקדות בפרטי הטיול שהמשתמש ביקש. השתמש בסיכום זה כדי להתחיל שיחה חדשה. אם אין פרטים חשובים, החזר מחרוזת ריקה. אל תשתמש בכותרות או סימני פיסוק מיותרים. הדגש מה המשתמש רוצה להתאים בתוכנית הטיול הנוכחית.";
        var summaryResponse = await chat.GenerateContentAsync(historyPrompt);
        return summaryResponse.Text?.Trim() ?? "";
    }

    // --- פונקציה מרכזית ---
    public async Task<string> GetResponseAsync(string userMessage)
    {
        try
        {
            Console.WriteLine($"קיבלתי הודעה: {userMessage}");

            // טעינת מצב השיחה מהמטמון. אם לא קיים, יוצרים מצב חדש
            var state = _cache.Get<ConversationState>(CacheKey) ?? new ConversationState { ChatSession = _generativeModel.StartChat() };
            string intent = ExtractIntent(userMessage);
            Console.WriteLine($"Intent זוהה: {intent}");

            string externalData = "";

            if (intent == "trip_plan")
            {
                string city = await ExtractCity(userMessage);
                if (string.IsNullOrEmpty(city))
                {
                    return "אנא ציין את שם העיר כדי שאוכל לתכנן עבורך טיול.";
                }

                state.LastCity = city; // שומרים את שם העיר במצב השיחה

                string duration = await ExtractTripDuration(userMessage);
                bool isWeekly = duration == "weekly";

                externalData = await _weatherService.GetWeather(city, isWeekly);

                if (externalData.Contains("אירעה שגיאה") || externalData.Contains("לא נמצאה"))
                {
                    return "לא הצלחתי לקבל נתוני מזג אוויר עבור העיר שציינת. אנא ודא שהשם נכון ונסה שוב.";
                }

                // --- לוגיקת יצירת תוכנית חדשה ---
                string systemPrompt = "אתה סוכן נסיעות מקצועי. ענה בעברית בצורה ברורה ומועילה. השתמש אך ורק במידע שסופק למטה מהמערכת החיצונית. אם אין מידע זמין, ציין זאת. אם המידע כולל נתונים על מזג אוויר, תכנן תוכנית טיול מפורטת שמתאימה לתנאים. **אל תכלול התייחסויות לשבת או 'שבת שלום' אלא אם כן התאריכים המפורטים בתכנית חלים בשבת.** אל תענה תשובה גנרית או תתנצל על חוסר מידע. אל תכתוב קוד או סוגרים מסולסלים.";
                string prompt = $"{systemPrompt}\n\nשאלת המשתמש: {userMessage}\n";
                prompt += !string.IsNullOrEmpty(externalData) ? externalData : "";
                prompt += "\nבנה תוכנית טיול מפורטת בהתאם לנתונים שסופקו. הדגש כיצד מזג האוויר משפיע על ההמלצות שלך וציין את טווח הטמפרטורות הצפוי לכל יום ויום בתוכנית.";

                var finalResponse = await state.ChatSession.GenerateContentAsync(prompt);

                if (!string.IsNullOrEmpty(finalResponse.Text))
                {
                    _cache.Set(CacheKey, state, TimeSpan.FromHours(1)); // שמירה של המצב המעודכן
                    Console.WriteLine($"תשובה התקבלה: {finalResponse.Text}");
                    return finalResponse.Text;
                }
                return "לא הצלחתי להביא תשובה כרגע.";
            }
            else // המשתמש המשיך את השיחה ללא מילות מפתח
            {
                // אם המצב הקודם היה תכנון טיול, נמשיך באותו הקשר
                if (!string.IsNullOrEmpty(state.LastCity))
                {
                    // נבדוק אם היסטוריית השיחה הגיעה לגודל קריטי ונסכם אותה
                    if (state.ChatSession.History.Count >= 6)
                    {
                        var summary = await SummarizeChat(state.ChatSession);
                        state.ChatSession = _generativeModel.StartChat(); // התחלת שיחה חדשה
                        await state.ChatSession.GenerateContentAsync($"סיכום שיחה קודמת: {summary}");
                    }

                    externalData = await _weatherService.GetWeather(state.LastCity, true);

                    // ניצור Prompt שמתאים את תוכנית הטיול הקיימת
                    string updatePrompt = $"המשך שיחה על תכנון טיול בעיר {state.LastCity}. המשתמש רוצה להתאים את התוכנית בהתאם להעדפות הבאות: '{userMessage}'. השתמש בנתוני מזג האוויר המצורפים לעידכון התוכנית. ענה עם תוכנית מעודכנת. הוסף לכותרת התוכנית את העיר וטווח התאריכים. **אל תכלול התייחסויות לשבת או 'שבת שלום' אלא אם כן התאריכים המפורטים בתכנית חלים בשבת.**";
                    var updatedResponse = await state.ChatSession.GenerateContentAsync($"{updatePrompt}\n{externalData}");

                    if (!string.IsNullOrEmpty(updatedResponse.Text))
                    {
                        _cache.Set(CacheKey, state, TimeSpan.FromHours(1)); // שמירה של המצב המעודכן
                        Console.WriteLine($"תשובה התקבלה: {updatedResponse.Text}");
                        return updatedResponse.Text;
                    }
                    return "לא הצלחתי לעדכן את התוכנית כרגע.";
                }
                else
                {
                    // אם אין קשר קודם, נחזור להתנהגות צ'אט רגילה
                    var response = await state.ChatSession.GenerateContentAsync(userMessage);
                    if (!string.IsNullOrEmpty(response.Text))
                    {
                        _cache.Set(CacheKey, state, TimeSpan.FromHours(1));
                        return response.Text;
                    }
                    return "לא הצלחתי להביא תשובה כרגע.";
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"שגיאה כללית: {ex}");
            return "אירעה שגיאה במערכת. אנא נסה שוב.";
        }
    }
}