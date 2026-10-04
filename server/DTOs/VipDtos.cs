namespace UsuariosRetirados.Server.DTOs;

public record VipEmployeeDto(
    int Id,
    string Cedula,
    string FullName,
    string? CreatedByUsername,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record CreateVipRequest(string Cedula, string FullName);

public record UpdateVipRequest(string Cedula, string FullName);

/// <summary>Fila de la lista VIP que apareció entre las coincidencias y fue omitida.</summary>
public record VipOmittedRow(string Cedula, string? FullName);
