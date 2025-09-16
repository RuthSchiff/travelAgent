using GenerativeAI;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using travelAgent.classes;
using Microsoft.OpenApi.Models;
using DotNetEnv; // ���� �� ����� ���

DotNetEnv.Env.Load(); // ���� �� ����� ��� ����
    
var builder = WebApplication.CreateBuilder(args);



// ����� ����� CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp",
        builder =>
        {
            builder.WithOrigins("http://localhost:3000")
                   .AllowAnyHeader()
                   .AllowAnyMethod();
        });
});


var geminiApiKey = builder.Configuration["Gemini:ApiKey"] ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");

if (string.IsNullOrEmpty(geminiApiKey))
{
    throw new InvalidOperationException("Gemini API key is not configured. Set it in appsettings.json or as an environment variable.");
}

builder.Services.AddSingleton(new GenerativeModel(geminiApiKey, "gemini-1.5-flash"));
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();

// ����� ������� ������� �����
builder.Services.AddScoped<WeatherService>(provider =>
    new WeatherService(
        provider.GetRequiredService<HttpClient>(),
        builder.Configuration["WeatherApi:ApiKey"] ?? Environment.GetEnvironmentVariable("WEATHER_API_KEY")
    ));
builder.Services.AddScoped<ConversationManager>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "TravelAgent API", Version = "v1" });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "TravelAgent API V1");
    });
}

// ����� CORS
app.UseCors("AllowReactApp");

app.MapPost("/chat", async (ChatRequest req, ConversationManager conversationManager) =>
{
    var response = await conversationManager.GetResponseAsync(req.Message);
    return Results.Ok(new { response = response });
});

app.UseHttpsRedirection();
app.Run();