using CashFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CashFlow.Server;

/// <summary>
/// Демо-пользователь для локальной разработки и проверок интерфейса: задаётся в конфигурации (Demo:Email / Demo:Password),
/// создаётся сервером при старте, вход выполняется одной кнопкой в приложении или по ссылке /dev/login в вебе.
/// Включать только на локальном сервере: пароль известен всем, кто видит конфигурацию.
/// </summary>
public sealed class DemoOptions
{
    public const string Section = "Demo";
    public string? Email { get; set; }
    public string? Password { get; set; }
    public bool Enabled => !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(Password);
}

public sealed record DemoCredentialsDto(string Email, string Password);

public static class DemoUser
{
    /// <summary>Создаёт демо-пользователя или обновляет ему пароль, чтобы он всегда совпадал с конфигурацией.</summary>
    public static async Task EnsureAsync(IServiceProvider scoped, CancellationToken ct = default)
    {
        var demo = scoped.GetRequiredService<IOptions<DemoOptions>>().Value;
        if (!demo.Enabled) return;
        var users = scoped.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(demo.Email!);
        if (user is null)
        {
            user = new ApplicationUser { UserName = demo.Email, Email = demo.Email, EmailConfirmed = true };
            var created = await users.CreateAsync(user, demo.Password!);
            if (!created.Succeeded) throw new InvalidOperationException("Демо-пользователь не создан: " + string.Join("; ", created.Errors.Select(e => e.Description)));
            return;
        }
        if (!await users.CheckPasswordAsync(user, demo.Password!))
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            var reset = await users.ResetPasswordAsync(user, token, demo.Password!);
            if (!reset.Succeeded) throw new InvalidOperationException("Пароль демо-пользователя не обновлён: " + string.Join("; ", reset.Errors.Select(e => e.Description)));
        }
    }

    /// <summary>GET /api/demo — реквизиты демо-пользователя для кнопки «Войти как демо» (404, если демо выключено).</summary>
    public static IEndpointRouteBuilder MapDemoApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/demo", (IOptions<DemoOptions> o) => o.Value.Enabled
            ? Results.Ok(new DemoCredentialsDto(o.Value.Email!, o.Value.Password!))
            : Results.NotFound());
        return app;
    }

    /// <summary>GET /dev/login — вход демо-пользователя по cookie для веб-хоста (только когда демо включено).</summary>
    public static IEndpointRouteBuilder MapDemoWebLogin(this IEndpointRouteBuilder app)
    {
        app.MapGet("/dev/login", async (IOptions<DemoOptions> o, SignInManager<ApplicationUser> signIn, string? returnUrl) =>
        {
            if (!o.Value.Enabled) return Results.NotFound();
            var result = await signIn.PasswordSignInAsync(o.Value.Email!, o.Value.Password!, isPersistent: true, lockoutOnFailure: false);
            return result.Succeeded ? Results.Redirect(string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl) : Results.Problem("Демо-вход не удался", statusCode: 500);
        });
        return app;
    }
}
