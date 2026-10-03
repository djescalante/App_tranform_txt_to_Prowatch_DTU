namespace UsuariosRetirados.Server.DTOs;

public record LoginRequest(string Username, string Password);

public record LoginResponse(string Token, string Username, string FullName, string Role);

public record UserDto(int Id, string Username, string FullName, string Role, bool IsActive, DateTime CreatedAt, DateTime? LastLoginAt);

public record CreateUserRequest(string Username, string FullName, string Password, string Role);

public record UpdateUserRequest(string FullName, string? Password, string Role, bool IsActive);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
