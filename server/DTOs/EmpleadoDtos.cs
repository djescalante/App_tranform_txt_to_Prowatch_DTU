namespace UsuariosRetirados.Server.DTOs;

public record PadronInfoResponse(
    bool Exists,
    string Path,
    long SizeBytes,
    string SizeFormatted,
    DateTime? LastModified,
    string SuggestedDate, // dd/MM/yyyy
    int TotalColumns,
    bool SchemaValid,
    List<string> SchemaIssues
);

public record FilterOptionsResponse(
    List<string> EstadosDisponibles,
    List<string> SociedadesFijas,
    string DefaultFechaEvento
);

public record PreviewRequest(
    string FechaEvento, // dd/MM/yyyy
    List<string> Sociedades,
    List<string> Estados,
    int Limit = 100
);

public record EmpleadoRowDto(
    string Estado,
    string Documento,
    string Sociedad,
    string FechaEvento
);

public record PreviewResponse(
    int TotalCoinciden,
    List<EmpleadoRowDto> Rows,
    long ElapsedMs,
    int VipOmittedCount,
    List<VipOmittedRow> VipOmittedRows
);

public record ProcessRequest(
    string FechaEvento, // dd/MM/yyyy
    List<string> Sociedades,
    List<string> Estados,
    bool EmitXlsx = true,
    bool EmitTsv = false,
    bool EmitDtu = true
);

public record ProcessResponse(
    int JobId,
    bool Success,
    int TotalCoinciden,
    long ElapsedMs,
    List<string> FilesGenerated,
    string Message,
    int VipOmittedCount
);

public record JobDto(
    int Id,
    DateTime CreatedAt,
    string CreatedByUsername,
    string? CreatedByFullName,
    string SourceFileName,
    string EventDate,
    string SelectedStates,
    string SelectedSocieties,
    int TotalMatchedRows,
    long ExecutionDurationMs,
    string? DtuFileName,
    string? XlsxFileName,
    string? TsvFileName,
    string Status,
    string? ErrorMessage,
    int VipOmittedCount,
    string? VipOmittedDetails
);
