using UserManagementAPI.Models;
using UserManagementAPI.Data;
using System.Text.RegularExpressions;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.AspNetCore.Diagnostics;
using System.IO;
using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<TokenValidationParameters>(provider =>
{
    var config = provider.GetRequiredService<IConfiguration>();
    return new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = config["Jwt:Issuer"],
        ValidAudience = config["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(config["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is missing")))
    };
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var tokenParams = context.HttpContext.RequestServices.GetRequiredService<TokenValidationParameters>();
                options.TokenValidationParameters = tokenParams;
                return Task.CompletedTask;
            }
        };
 });

builder.Services.AddAuthorization(); 
var app = builder.Build();

// Global error handler
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exceptionHandlerPathFeature = context.Features.Get<IExceptionHandlerPathFeature>();
        var error = exceptionHandlerPathFeature?.Error;

        Console.WriteLine($"❌ Unhandled Exception: {error?.Message}");

        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";

        var errorResponse = new { error = "Internal server error." };
        await context.Response.WriteAsJsonAsync(errorResponse);
    });
});

app.UseMiddleware<RequestResponseLoggingMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.Use(async (context, next) =>
{
    var request = context.Request;
    Console.WriteLine($"➡️ Request: {request.Method} {request.Path}");

    // Capture the response
    var originalBodyStream = context.Response.Body;
    using var responseBody = new MemoryStream();
    context.Response.Body = responseBody;

    await next();

    context.Response.Body.Seek(0, SeekOrigin.Begin);
    var responseText = await new StreamReader(context.Response.Body).ReadToEndAsync();
    context.Response.Body.Seek(0, SeekOrigin.Begin);

    Console.WriteLine($"⬅️ Response: {context.Response.StatusCode} {responseText}");

    await responseBody.CopyToAsync(originalBodyStream);
});

app.UseStatusCodePages(async context =>
{
    var response = context.HttpContext.Response;
    if (response.StatusCode == 404)
    {
        response.ContentType = "application/json";
        await response.WriteAsJsonAsync(new { error = "Resource not found." });
    }
});

//GET: All users with optional paging
app.MapGet("/users", (int? skip, int? take) =>
{
    try
    {
        var users = UserStore.Users.AsReadOnly();

        // Paging logic
        var paged = users
            .Skip(skip ?? 0)
            .Take(take ?? users.Count)
            .ToList();

        return Results.Ok(paged);
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = $"Fehler beim Abrufen der Benutzer: {ex.Message}" }, statusCode: 500);
    }
}).RequireAuthorization();

//GET: User by ID
app.MapGet("/users/{id}", (int id) =>
{
    try
    {
        var user = UserStore.Users.FirstOrDefault(u => u.Id == id);
        return user is null
            ? Results.NotFound($"Benutzer mit ID {id} nicht gefunden.")
            : Results.Ok(user);
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = $"Fehler beim Abrufen des Benutzers: {ex.Message}" }, statusCode: 500);
    }
}).RequireAuthorization();

//POST: Add new user with validation
app.MapPost("/users", (User user) =>
{
    try
    {
        var error = ValidateUser(user);
        if (error is not null)
            return Results.BadRequest(error);

        user.Id = UserStore.Users.Count > 0 ? UserStore.Users.Max(u => u.Id) + 1 : 1;
        UserStore.Users.Add(user);
        return Results.Created($"/users/{user.Id}", user);
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = $"Fehler beim Hinzufügen des Benutzers: {ex.Message}" }, statusCode: 500);

    }
}).RequireAuthorization();

//PUT: Update user
app.MapPut("/users/{id}", (int id, User updatedUser) =>
{
    try
    {
        var user = UserStore.Users.FirstOrDefault(u => u.Id == id);
        if (user is null)
            return Results.NotFound($"Benutzer mit ID {id} nicht gefunden.");

        var error = ValidateUser(updatedUser);
        if (error is not null)
            return Results.BadRequest(error);

        user.Username = updatedUser.Username;
        user.Email = updatedUser.Email;
        return Results.Ok(user);
    }
    catch (Exception ex)
    {
       return Results.Json(new { error = $"Fehler beim Aktualisieren des Benutzers: {ex.Message}" }, statusCode: 500);
    }
}).RequireAuthorization();

//DELETE: Remove user
app.MapDelete("/users/{id}", (int id) =>
{
    try
    {
        var user = UserStore.Users.FirstOrDefault(u => u.Id == id);
        if (user is null)
            return Results.NotFound($"Benutzer mit ID {id} nicht gefunden.");

        UserStore.Users.Remove(user);
        return Results.NoContent();
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = $"Fehler beim Löschen des Benutzers: {ex.Message}" }, statusCode: 500);

    }
}).RequireAuthorization();

app.Run();

//Validation helper
string? ValidateUser(User user)
{
    if (string.IsNullOrWhiteSpace(user.Username))
        return "Username darf nicht leer sein.";

    if (string.IsNullOrWhiteSpace(user.Email))
        return "Email darf nicht leer sein.";

    if (!Regex.IsMatch(user.Email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
        return "Ungültiges Email-Format.";

    return null;
}

public class RequestResponseLoggingMiddleware
{
    private readonly RequestDelegate _next;

    public RequestResponseLoggingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var originalBodyStream = context.Response.Body;
        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        await _next(context); // Lass andere Middleware zuerst arbeiten

        // Nur loggen, wenn der Stream noch lesbar ist
        if (responseBody.CanSeek)
        {
            responseBody.Seek(0, SeekOrigin.Begin);
            var responseText = await new StreamReader(responseBody).ReadToEndAsync();
            responseBody.Seek(0, SeekOrigin.Begin);

            Console.WriteLine($"⬅️ Response: {context.Response.StatusCode} {responseText}");
        }

        await responseBody.CopyToAsync(originalBodyStream);
        context.Response.Body = originalBodyStream;
    }
}
