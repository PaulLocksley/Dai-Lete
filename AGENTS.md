# AGENTS.md - Dai-Lete Development Guide

## Build/Test Commands
- **Build**: `dotnet build Dai-Lete/Dai-Lete.csproj`
- **Run**: `dotnet run --project Dai-Lete/Dai-Lete.csproj`
- **Publish**: `dotnet publish Dai-Lete/Dai-Lete.csproj -c Release`
- **Docker**: `docker build -t dai-lete .`
- **No formal test framework found** - add tests using xUnit or NUnit if needed

## Project Structure
- **Main project**: `Dai-Lete/` (ASP.NET Core 8.0 web app)
- **Controllers**: API endpoints for podcast management
- **Models**: Data models (Podcast, PodcastMetadata, etc.)
- **Services**: Business logic (PodcastServices, XmlService, etc.)
- **Repositories**: Data access layer with SQLite/Dapper

## Code Style Guidelines
- **Namespace**: Use `Dai_Lete.{Folder}` pattern (underscores, not hyphens)
- **Nullable**: Enabled - use `?` for nullable types, handle null cases
- **Implicit usings**: Enabled - avoid redundant using statements
- **Properties**: Use public fields for simple models (e.g., `public Uri InUri;`)
- **Methods**: PascalCase for public, camelCase for parameters
- **Error handling**: Use exceptions for data errors, return HTTP status codes in controllers
- **Comments**: Minimal - use `//todo:` for known issues
- **Database**: Use Dapper with parameterized queries, SQLite connection via `SqLite.Connection()`