# One Dockerfile, two runnable targets: `api` and `seeder`.
# They share the restore and build stages, so a compose build compiles the solution once.
# The host needs no .NET SDK.

# ---- restore: copy only project/props files so the package layer is cached until a
# ---- dependency changes, not on every source edit.
FROM mcr.microsoft.com/dotnet/sdk:8.0.425-bookworm-slim AS restore
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY src/Dispatch.Domain/Dispatch.Domain.csproj src/Dispatch.Domain/
COPY src/Dispatch.Application/Dispatch.Application.csproj src/Dispatch.Application/
COPY src/Dispatch.Infrastructure/Dispatch.Infrastructure.csproj src/Dispatch.Infrastructure/
COPY src/Dispatch.Api/Dispatch.Api.csproj src/Dispatch.Api/
COPY src/Dispatch.Seeder/Dispatch.Seeder.csproj src/Dispatch.Seeder/

# Sequential on purpose: both projects restore the shared libraries, and parallel
# restores of the same project race on obj/project.assets.json.
RUN dotnet restore src/Dispatch.Api/Dispatch.Api.csproj \
    && dotnet restore src/Dispatch.Seeder/Dispatch.Seeder.csproj

# ---- build
FROM restore AS build
COPY src/ src/
RUN dotnet build src/Dispatch.Api/Dispatch.Api.csproj -c Release --no-restore \
    && dotnet build src/Dispatch.Seeder/Dispatch.Seeder.csproj -c Release --no-restore

FROM build AS publish-api
RUN dotnet publish src/Dispatch.Api/Dispatch.Api.csproj -c Release --no-build -o /out

FROM build AS publish-seeder
RUN dotnet publish src/Dispatch.Seeder/Dispatch.Seeder.csproj -c Release --no-build -o /out

# ---- targets
FROM mcr.microsoft.com/dotnet/aspnet:8.0.31-bookworm-slim AS api
WORKDIR /app
COPY --from=publish-api /out .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Dispatch.Api.dll"]

FROM mcr.microsoft.com/dotnet/runtime:8.0.31-bookworm-slim AS seeder
WORKDIR /app
COPY --from=publish-seeder /out .
USER $APP_UID
ENTRYPOINT ["dotnet", "Dispatch.Seeder.dll"]
