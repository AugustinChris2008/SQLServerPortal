# syntax=docker/dockerfile:1.7

FROM node:24-alpine AS web-build
WORKDIR /source/web
COPY src/SqlPortal.Web/package.json src/SqlPortal.Web/package-lock.json ./
RUN npm ci
COPY src/SqlPortal.Web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS api-build
WORKDIR /source
COPY Directory.Build.props ./
COPY src/SqlPortal.Api/SqlPortal.Api.csproj src/SqlPortal.Api/
RUN dotnet restore src/SqlPortal.Api/SqlPortal.Api.csproj
COPY src/SqlPortal.Api/ src/SqlPortal.Api/
COPY --from=web-build /source/web/dist/ src/SqlPortal.Api/wwwroot/
RUN dotnet publish src/SqlPortal.Api/SqlPortal.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
RUN apk add --no-cache icu-libs icu-data-full
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080
COPY --from=api-build /app/publish/ ./
USER app
ENTRYPOINT ["dotnet", "SqlPortal.Api.dll"]

