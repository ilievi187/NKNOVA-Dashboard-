# Nolvus.Services tests

Run the signed-download-URL redaction regression test from the repository root:

```bash
dotnet run --project Nolvus.Services.Tests/Nolvus.Services.Tests.csproj
```

Expected output includes:

```text
PASS: BaseFileDownloader redacts signed download URLs.
```
