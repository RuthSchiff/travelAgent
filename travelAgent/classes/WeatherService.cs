using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

public class WeatherService
{
    private readonly HttpClient _httpClient;
    private readonly string _weatherApiKey;

    public WeatherService(HttpClient httpClient, string weatherApiKey)
    {
        _httpClient = httpClient;
        _weatherApiKey = weatherApiKey;
    }

    public async Task<string> GetWeather(string city, bool isWeekly = false)
    {
        try
        {
            string url;
            if (isWeekly)
            {
                url = $"https://api.openweathermap.org/data/2.5/forecast?q={city}&appid={_weatherApiKey}&units=metric&lang=he";
            }
            else
            {
                url = $"https://api.openweathermap.org/data/2.5/weather?q={city}&appid={_weatherApiKey}&units=metric&lang=he";
            }

            var response = await _httpClient.GetStringAsync(url);
            var data = JsonDocument.Parse(response);

            return isWeekly ? ParseWeeklyWeather(data, city) : ParseCurrentWeather(data, city);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"שגיאה בקבלת מזג האוויר: {ex.Message}");
            return "אירעה שגיאה בנתוני מזג האוויר.";
        }
    }

    private string ParseCurrentWeather(JsonDocument data, string city)
    {
        try
        {
            string temp = data.RootElement.GetProperty("main").GetProperty("temp").GetDecimal().ToString("0.0");
            string description = data.RootElement.GetProperty("weather")[0].GetProperty("description").GetString();
            return $"מזג האוויר הנוכחי ב{city}: {description}, עם טמפרטורה של {temp}°C.";
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"שגיאת JSON בפרסור מזג אוויר: {ex.Message}");
            return "אירעה שגיאה בעיבוד נתוני מזג האוויר.";
        }
    }

    private string ParseWeeklyWeather(JsonDocument data, string city)
    {
        try
        {
            if (!data.RootElement.TryGetProperty("list", out var forecastList))
            {
                return "לא נמצאה תחזית שבועית זמינה.";
            }

            var dailyForecasts = new Dictionary<string, (decimal minTemp, decimal maxTemp, string description)>();

            foreach (var forecastItem in forecastList.EnumerateArray())
            {
                var dtText = forecastItem.GetProperty("dt_txt").GetString();
                if (dtText == null) continue;

                var date = DateTime.Parse(dtText).Date.ToShortDateString();
                var temp = forecastItem.GetProperty("main").GetProperty("temp").GetDecimal();
                var description = forecastItem.GetProperty("weather")[0].GetProperty("description").GetString();

                if (!dailyForecasts.ContainsKey(date))
                {
                    dailyForecasts[date] = (temp, temp, description);
                }
                else
                {
                    var existing = dailyForecasts[date];
                    decimal currentMin = Math.Min(existing.minTemp, temp);
                    decimal currentMax = Math.Max(existing.maxTemp, temp);

                    dailyForecasts[date] = (currentMin, currentMax, description);
                }
            }

            string result = $"תחזית מזג האוויר ל-5 ימים עבור {city}:\n";
            foreach (var dailyEntry in dailyForecasts.OrderBy(d => DateTime.Parse(d.Key)).Take(5))
            {
                string date = dailyEntry.Key;
                decimal minTemp = dailyEntry.Value.minTemp;
                decimal maxTemp = dailyEntry.Value.maxTemp;
                string description = dailyEntry.Value.description;

                result += $"- {date}: {description}, טווח טמפרטורות: {minTemp:0}°C - {maxTemp:0}°C\n";
            }

            return result;
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"שגיאת JSON בפרסור תחזית שבועית: {ex.Message}");
            return "אירעה שגיאה בעיבוד נתוני התחזית השבועית.";
        }
    }
}