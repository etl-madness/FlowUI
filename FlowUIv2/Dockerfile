# syntax=docker/dockerfile:1
# =============================================================================
# FlowUIv2 - Linux Container Dockerfile (Project Directory Context)
# Target Framework: .NET 10.0 (ASP.NET Core Interactive Server Blazor)
# Build from FlowUIv2 project directory:
#   docker build -t flowuiv2:latest .
# =============================================================================

ARG DOTNET_VERSION=10.0

# Stage 1: Restore & Publish
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["FlowUIv2.csproj", "./"]
RUN dotnet restore "FlowUIv2.csproj"

COPY . .
RUN dotnet publish "FlowUIv2.csproj" \
    -c ${BUILD_CONFIGURATION} \
    -o /app/publish \
    /p:UseAppHost=false

# Stage 2: Runtime Image
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS final
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    FLOW_UI_PORT=5185 \
    ASPNETCORE_URLS=http://+:5185 \
    FLOW_API_HOST=http://host.docker.internal \
    FLOW_API_PORT=8080 \
    FLOW_UI_AUTH_MODE=ApiBasic

EXPOSE 5185

COPY --from=build /app/publish .

USER $APP_UID
ENTRYPOINT ["dotnet", "FlowUIv2.dll"]
