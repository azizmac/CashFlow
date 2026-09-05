using Microsoft.AspNetCore.Identity;

namespace CashFlow.Server;

/// <summary>Тексты ошибок Identity на русском: уходят и в веб-формы, и в ответы /api/auth/register.</summary>
public sealed class RussianIdentityErrorDescriber : IdentityErrorDescriber
{
    private static IdentityError E(string code, string description) => new() { Code = code, Description = description };

    public override IdentityError DefaultError() => E(nameof(DefaultError), "Произошла неизвестная ошибка.");
    public override IdentityError ConcurrencyFailure() => E(nameof(ConcurrencyFailure), "Данные изменены другим запросом, повторите операцию.");
    public override IdentityError PasswordMismatch() => E(nameof(PasswordMismatch), "Неверный пароль.");
    public override IdentityError InvalidToken() => E(nameof(InvalidToken), "Недействительный код подтверждения.");
    public override IdentityError RecoveryCodeRedemptionFailed() => E(nameof(RecoveryCodeRedemptionFailed), "Код восстановления не подошёл.");
    public override IdentityError LoginAlreadyAssociated() => E(nameof(LoginAlreadyAssociated), "Этот внешний вход уже привязан к другому пользователю.");
    public override IdentityError InvalidUserName(string? userName) => E(nameof(InvalidUserName), $"Имя пользователя «{userName}» недопустимо: разрешены только буквы и цифры.");
    public override IdentityError InvalidEmail(string? email) => E(nameof(InvalidEmail), $"E-mail «{email}» указан неверно.");
    public override IdentityError DuplicateUserName(string userName) => E(nameof(DuplicateUserName), $"Пользователь «{userName}» уже существует.");
    public override IdentityError DuplicateEmail(string email) => E(nameof(DuplicateEmail), $"E-mail «{email}» уже зарегистрирован.");
    public override IdentityError InvalidRoleName(string? role) => E(nameof(InvalidRoleName), $"Недопустимое имя роли «{role}».");
    public override IdentityError DuplicateRoleName(string role) => E(nameof(DuplicateRoleName), $"Роль «{role}» уже существует.");
    public override IdentityError UserAlreadyHasPassword() => E(nameof(UserAlreadyHasPassword), "У пользователя уже задан пароль.");
    public override IdentityError UserLockoutNotEnabled() => E(nameof(UserLockoutNotEnabled), "Блокировка для этого пользователя не включена.");
    public override IdentityError UserAlreadyInRole(string role) => E(nameof(UserAlreadyInRole), $"Пользователь уже в роли «{role}».");
    public override IdentityError UserNotInRole(string role) => E(nameof(UserNotInRole), $"Пользователь не состоит в роли «{role}».");
    public override IdentityError PasswordTooShort(int length) => E(nameof(PasswordTooShort), $"Пароль должен быть не короче {length} символов.");
    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) => E(nameof(PasswordRequiresUniqueChars), $"В пароле должно быть не меньше {uniqueChars} разных символов.");
    public override IdentityError PasswordRequiresNonAlphanumeric() => E(nameof(PasswordRequiresNonAlphanumeric), "В пароле должен быть хотя бы один спецсимвол (например, ! или _).");
    public override IdentityError PasswordRequiresDigit() => E(nameof(PasswordRequiresDigit), "В пароле должна быть хотя бы одна цифра.");
    public override IdentityError PasswordRequiresLower() => E(nameof(PasswordRequiresLower), "В пароле должна быть хотя бы одна строчная буква.");
    public override IdentityError PasswordRequiresUpper() => E(nameof(PasswordRequiresUpper), "В пароле должна быть хотя бы одна заглавная буква.");
}
