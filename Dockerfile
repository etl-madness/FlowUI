# syntax=docker/dockerfile:1
# =============================================================================
# FlowUIv2 - Linux Container Dockerfile (Default)
# Target Framework: .NET 10.0 (ASP.NET Core Interactive Server Blazor)
# Build from repository root:
#   docker build -t flowuiv2:latest -f Dockerfile .
# =============================================================================

ARG DOTNET_VERSION=10.0

# Stage 1: Restore & Publish
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["FlowUIv2/FlowUIv2.csproj", "FlowUIv2/"]
RUN dotnet restore "FlowUIv2/FlowUIv2.csproj"

COPY ["FlowUIv2/", "FlowUIv2/"]
WORKDIR "/src/FlowUIv2"
RUN dotnet publish "FlowUIv2.csproj" \
    -c ${BUILD_CONFIGURATION} \
    -o /app/publish \
    /p:UseAppHost=false

# Stage 2: Runtime Image
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS final
WORKDIR /app

# Default UI port and API connection settings inside container
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
