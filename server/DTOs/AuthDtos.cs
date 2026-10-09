namespace PWExtendedApp.Server.DTOs;

public record LoginRequest(string Username, string Password);

public record LoginResponse(string Token, string Username, string FullName, string Role, bool MustChangePassword = false);

public record UserDto(int Id, string Username, string FullName, string Role, bool IsActive, DateTime CreatedAt, DateTime? LastLoginAt,
    bool IsPrincipal = false, bool MustChangePassword = false, bool IsLockedOut = false);

public record CreateUserRequest(string Username, string FullName, string Password, string Role);

public record UpdateUserRequest(string FullName, string? Password, string Role, bool IsActive, bool Unlock = false);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
