# PCPI // Retro Minimals — v2.0 ENGINE

Instalador de software portable y ultraligero para Windows 10/11, con estética
de terminal retro (MS-DOS / cyberpunk). Un único archivo `Program.cs` (WinForms,
.NET 8), sin dependencias de terceros.

## Características

- **14 pestañas de categorías**: Antivirus, Navegadores, Descompresores, Ofimática,
  IA, Multimedia, Editar Fotos, Editar Videos, Drivers, **Drivers Backup**,
  Streaming, Gaming, Música, Chat.
- Instalación **asíncrona** vía `winget` (`cmd.exe`), con **barra de progreso y
  porcentaje por aplicación** en el color del tema.
- **Módulo Drivers Backup**:
  - Copiar/exportar los controladores del equipo con `DISM` a
    `Escritorio\PCPI_Drivers_Backup` (para llevar en un USB antes de formatear).
  - Restaurar drivers desde una carpeta con `pnputil /add-driver ... /subdirs /install`.
- **5 temas de color** intercambiables en caliente: RED, GREEN, AMBER, CYAN, WHITE.
- **Efecto CRT** (scanlines) con activación/desactivación.
- Recuerda entre sesiones el tema y el estado del CRT (`%APPDATA%\PCPI\pcpi.cfg`).
- Consola de log con marcas de tiempo `[HH:mm:ss]`.

## Compilar

```
dotnet build -c Release
dotnet run
```

## Generar el .exe portable (un solo archivo)

```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

Salida: `bin/Release/net8.0-windows/win-x64/publish/PCPI.exe`

> Muchas instalaciones (winget silencioso, DISM, pnputil) requieren ejecutar
> PCPI **como administrador**.
